using System.Collections.Generic;
using UnityEngine;

namespace SchoolEscape.Multiplayer
{
    public sealed class ServerRewindHistory
    {
        public const double MaximumRewindSeconds = .75d;
        private const double FutureToleranceSeconds = .1d;
        private const int FrameCapacity = 64;
        private const int MaximumEnemies = 32;
        private const int MaximumActors = 2;

        private sealed class HistoryFrame
        {
            public int Tick;
            public double ServerTime;
            public bool Valid;
            public int EnemyCount;
            public int ActorCount;
            public readonly EntitySample[] Enemies = new EntitySample[MaximumEnemies];
            public readonly EntitySample[] Actors = new EntitySample[MaximumActors];
        }

        private struct EntitySample
        {
            public int Id;
            public Vector3 Position;
        }

        private readonly HistoryFrame[] frames = new HistoryFrame[FrameCapacity];
        private int nextFrame;
        private int lastRecordedTick = int.MinValue;

        public int TotalRequests { get; private set; }
        public int AcceptedRequests { get; private set; }
        public int RejectedRequests { get; private set; }
        public int RewindAcceptedHits { get; private set; }
        public int CurrentStateAcceptedHits { get; private set; }
        public int LastRewindHits { get; private set; }
        public int LastCurrentStateHits { get; private set; }
        public double LastRequestAgeMilliseconds { get; private set; }
        public string LastResult { get; private set; } = "Sin solicitudes";

        public ServerRewindHistory()
        {
            for (int index = 0; index < frames.Length; index++)
                frames[index] = new HistoryFrame();
        }

        public void RecordFrame(SchoolEscapePrototype session, int tick, double serverTime)
        {
            if (session == null || tick == lastRecordedTick) return;
            lastRecordedTick = tick;

            HistoryFrame frame = frames[nextFrame];
            nextFrame = (nextFrame + 1) % frames.Length;
            frame.Tick = tick;
            frame.ServerTime = serverTime;
            frame.Valid = true;
            frame.EnemyCount = 0;
            frame.ActorCount = 0;

            IReadOnlyList<PrototypeEnemy> enemies = session.ActiveEnemies;
            for (int index = 0; index < enemies.Count && frame.EnemyCount < MaximumEnemies; index++)
            {
                PrototypeEnemy enemy = enemies[index];
                if (enemy == null) continue;
                frame.Enemies[frame.EnemyCount++] = new EntitySample
                {
                    Id = enemy.SnapshotId,
                    Position = enemy.transform.position
                };
            }

            RecordActor(frame, session.Player);
            RecordActor(frame, session.Ally);
        }

        public bool TryEvaluateFlash(
            SchoolEscapePrototype session,
            PrototypeActor actor,
            Vector3 direction,
            double requestedServerTime,
            double currentServerTime,
            out List<PrototypeEnemy> historicalHits,
            out int currentStateHits,
            out string rejectionReason)
        {
            historicalHits = new List<PrototypeEnemy>();
            currentStateHits = 0;
            rejectionReason = string.Empty;
            TotalRequests++;

            double age = currentServerTime - requestedServerTime;
            LastRequestAgeMilliseconds = age * 1000d;
            if (requestedServerTime <= 0d || age > MaximumRewindSeconds || age < -FutureToleranceSeconds)
            {
                RejectedRequests++;
                LastRewindHits = 0;
                LastCurrentStateHits = 0;
                rejectionReason = age > MaximumRewindSeconds
                    ? $"solicitud demasiado antigua ({age * 1000d:0} ms)"
                    : "tiempo de acción inválido";
                LastResult = $"Rechazado: {rejectionReason}";
                return false;
            }

            if (!TryGetFrames(requestedServerTime, out HistoryFrame older, out HistoryFrame newer, out float blend))
            {
                RejectedRequests++;
                rejectionReason = "sin historial para ese instante";
                LastResult = $"Rechazado: {rejectionReason}";
                return false;
            }

            Vector3 origin = TrySampleEntity(actor.GetInstanceID(), older.Actors, older.ActorCount, newer.Actors, newer.ActorCount, blend, out Vector3 historicalOrigin)
                ? historicalOrigin
                : actor.transform.position;
            IReadOnlyList<PrototypeEnemy> enemies = session.ActiveEnemies;
            for (int index = 0; index < enemies.Count; index++)
            {
                PrototypeEnemy enemy = enemies[index];
                if (enemy == null) continue;

                if (IsInsideFlash(origin, direction, enemy.transform.position)) currentStateHits++;
                bool existedAtRequestedTime = TrySampleEntity(
                    enemy.SnapshotId,
                    older.Enemies,
                    older.EnemyCount,
                    newer.Enemies,
                    newer.EnemyCount,
                    blend,
                    out Vector3 historicalPosition);
                if (existedAtRequestedTime && IsInsideFlash(origin, direction, historicalPosition)) historicalHits.Add(enemy);
            }

            AcceptedRequests++;
            LastRewindHits = historicalHits.Count;
            LastCurrentStateHits = currentStateHits;
            RewindAcceptedHits += historicalHits.Count;
            CurrentStateAcceptedHits += currentStateHits;
            LastResult = $"Aceptado · rewind {historicalHits.Count} / actual {currentStateHits} · {LastRequestAgeMilliseconds:0} ms";
            return true;
        }

        private static void RecordActor(HistoryFrame frame, PrototypeActor actor)
        {
            if (actor == null || frame.ActorCount >= MaximumActors) return;
            frame.Actors[frame.ActorCount++] = new EntitySample
            {
                Id = actor.GetInstanceID(),
                Position = actor.transform.position
            };
        }

        private bool TryGetFrames(double requestedTime, out HistoryFrame older, out HistoryFrame newer, out float blend)
        {
            older = null;
            newer = null;
            for (int index = 0; index < frames.Length; index++)
            {
                HistoryFrame frame = frames[index];
                if (!frame.Valid) continue;
                if (frame.ServerTime <= requestedTime && (older == null || frame.ServerTime > older.ServerTime)) older = frame;
                if (frame.ServerTime >= requestedTime && (newer == null || frame.ServerTime < newer.ServerTime)) newer = frame;
            }

            if (older == null && newer == null)
            {
                blend = 0f;
                return false;
            }
            if (older == null) older = newer;
            if (newer == null) newer = older;
            double duration = newer.ServerTime - older.ServerTime;
            blend = duration <= double.Epsilon ? 0f : Mathf.Clamp01((float)((requestedTime - older.ServerTime) / duration));
            return true;
        }

        private static bool TrySampleEntity(
            int id,
            EntitySample[] olderSamples,
            int olderCount,
            EntitySample[] newerSamples,
            int newerCount,
            float blend,
            out Vector3 position)
        {
            bool hasOlder = TryFind(id, olderSamples, olderCount, out Vector3 olderPosition);
            bool hasNewer = TryFind(id, newerSamples, newerCount, out Vector3 newerPosition);
            if (hasOlder && hasNewer)
            {
                position = Vector3.Lerp(olderPosition, newerPosition, blend);
                return true;
            }
            if (hasOlder)
            {
                position = olderPosition;
                return true;
            }
            if (hasNewer)
            {
                position = newerPosition;
                return true;
            }
            position = default;
            return false;
        }

        private static bool TryFind(int id, EntitySample[] samples, int count, out Vector3 position)
        {
            for (int index = 0; index < count; index++)
            {
                if (samples[index].Id != id) continue;
                position = samples[index].Position;
                return true;
            }
            position = default;
            return false;
        }

        private static bool IsInsideFlash(Vector3 origin, Vector3 direction, Vector3 target)
        {
            Vector3 delta = target - origin;
            delta.y = 0f;
            return delta.magnitude <= 7f && delta.sqrMagnitude > .001f && Vector3.Angle(direction, delta) <= 35f;
        }
    }
}
