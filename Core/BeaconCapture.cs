namespace TidalNexus.StandaloneServer.Core
{

    public enum BeaconFaction
    {
        None = -1,
        Nautilus = 0,
        Serran = 1,
        Azularis = 2,
    }

    public readonly struct BeaconCrowd
    {
        public static readonly BeaconCrowd Empty = new BeaconCrowd(0, 0, 0);

        private readonly int _nautilus;
        private readonly int _serran;
        private readonly int _azularis;

        public BeaconCrowd(int nautilus, int serran, int azularis)
        {
            _nautilus = nautilus > 0 ? nautilus : 0;
            _serran = serran > 0 ? serran : 0;
            _azularis = azularis > 0 ? azularis : 0;
        }

        public int Nautilus => _nautilus;

        public int Serran => _serran;

        public int Azularis => _azularis;

        public int CountOf(BeaconFaction faction)
        {
            switch (faction)
            {
                case BeaconFaction.Nautilus: return _nautilus;
                case BeaconFaction.Serran: return _serran;
                case BeaconFaction.Azularis: return _azularis;
                default: return 0;
            }
        }

        public bool TryLeader(out BeaconFaction faction, out int crew)
        {
            faction = BeaconFaction.None;
            crew = 0;
            bool tied = false;

            for (int f = 0; f < 3; f++)
            {
                int count = CountOf((BeaconFaction)f);

                if (count > crew)
                {
                    faction = (BeaconFaction)f;
                    crew = count;
                    tied = false;
                }
                else if (count == crew && crew > 0)
                {
                    tied = true;
                }
            }

            if (tied || crew == 0)
            {
                faction = BeaconFaction.None;
                crew = 0;
                return false;
            }

            return true;
        }
    }

    public readonly struct BeaconState
    {
        public BeaconState(float percentage, BeaconFaction owner, BeaconFaction dominant)
        {
            Percentage = percentage;
            Owner = owner;
            Dominant = dominant;
        }

        public float Percentage { get; }

        public BeaconFaction Owner { get; }

        public BeaconFaction Dominant { get; }
    }

    public readonly struct BeaconTick
    {
        public BeaconTick(float percentage, BeaconFaction owner, BeaconFaction dominant,
            bool captured)
        {
            Percentage = percentage;
            Owner = owner;
            Dominant = dominant;
            Captured = captured;
        }

        public float Percentage { get; }

        public BeaconFaction Owner { get; }

        public BeaconFaction Dominant { get; }

        public bool Captured { get; }
    }

    public static class BeaconCapture
    {
        public const float Complete = 100f;

        public static BeaconTick Advance(BeaconState state, BeaconCrowd crowd,
            float captureRate, float deltaTime)
        {
            float percentage = state.Percentage;

            if (percentage < 0f)
            {
                percentage = 0f;
            }
            else if (percentage > Complete)
            {
                percentage = Complete;
            }

            if (deltaTime <= 0f || captureRate <= 0f
                || !crowd.TryLeader(out BeaconFaction attacker, out int crew))
            {

                return new BeaconTick(percentage, state.Owner,
                    percentage <= 0f ? BeaconFaction.None : state.Dominant, false);
            }

            float step = captureRate * crew * deltaTime;
            BeaconFaction owner = state.Owner;
            bool captured = false;

            bool theirsToFill = owner == attacker
                || (owner == BeaconFaction.None
                    && (state.Dominant == BeaconFaction.None || state.Dominant == attacker));

            if (theirsToFill)
            {
                percentage += step;
                if (percentage > Complete)
                {
                    percentage = Complete;
                }
            }
            else
            {

                percentage -= step;
                if (percentage <= 0f)
                {
                    percentage = 0f;
                    owner = BeaconFaction.None;
                }
            }

            if (percentage >= Complete && owner != attacker)
            {
                owner = attacker;
                captured = true;
            }

            return new BeaconTick(percentage, owner, attacker, captured);
        }
    }
}
