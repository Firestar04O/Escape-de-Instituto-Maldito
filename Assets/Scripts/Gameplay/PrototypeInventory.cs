public sealed class PrototypeInventory
{
    public const int MaxBatteries = 3;
    public const int MaxBandages = 1;

    public int Batteries { get; private set; }
    public int Bandages { get; private set; }

    public PrototypeInventory() => Reset();

    public void Reset()
    {
        Batteries = 2;
        Bandages = 0;
    }

    public void AddBattery()
    {
        if (Batteries < MaxBatteries) Batteries++;
    }

    public void AddBandage() => Bandages = MaxBandages;

    public bool TryUseBattery()
    {
        if (Batteries <= 0) return false;
        Batteries--;
        return true;
    }

    public bool TryUseBandage()
    {
        if (Bandages <= 0) return false;
        Bandages--;
        return true;
    }
}
