using System;

namespace SchoolEscape.Rooms
{
    public enum RescueDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    public static class RescueDirectionUtility
    {
        public static RescueDirection Opposite(RescueDirection direction)
        {
            switch (direction)
            {
                case RescueDirection.Up: return RescueDirection.Down;
                case RescueDirection.Down: return RescueDirection.Up;
                case RescueDirection.Left: return RescueDirection.Right;
                default: return RescueDirection.Left;
            }
        }

        public static bool TryParseEntrance(string objectName, out RescueDirection direction) =>
            TryParseSuffix(objectName, "EntradaRescate", out direction);

        public static bool TryParseDoor(string objectName, out RescueDirection direction)
        {
            string normalized = objectName.Replace("(", string.Empty).Replace(")", string.Empty).Replace(" ", string.Empty);
            return TryParseSuffix(normalized, "Puerta", out direction);
        }

        private static bool TryParseSuffix(string value, string prefix, out RescueDirection direction)
        {
            direction = RescueDirection.Up;
            string normalized = value
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace("(", string.Empty)
                .Replace(")", string.Empty);
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string suffix = normalized.Substring(prefix.Length);
            if (suffix.Equals("Arriba", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Up", StringComparison.OrdinalIgnoreCase))
                direction = RescueDirection.Up;
            else if (suffix.Equals("Abajo", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Down", StringComparison.OrdinalIgnoreCase))
                direction = RescueDirection.Down;
            else if (suffix.Equals("Izquierda", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Left", StringComparison.OrdinalIgnoreCase))
                direction = RescueDirection.Left;
            else if (suffix.Equals("Derecha", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Right", StringComparison.OrdinalIgnoreCase))
                direction = RescueDirection.Right;
            else
                return false;
            return true;
        }
    }
}
