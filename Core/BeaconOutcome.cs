namespace TidalNexus.StandaloneServer.Core
{

    public static class BeaconOutcome
    {
        public static BeaconFaction Winner(float nautilus, float serran, float azularis)
        {
            var points = new[] { nautilus, serran, azularis };

            int best = 0;
            int sharingBest = 1;

            for (int faction = 1; faction < points.Length; faction++)
            {
                if (points[faction] > points[best])
                {
                    best = faction;
                    sharingBest = 1;
                }
                else if (points[faction] == points[best])
                {
                    sharingBest++;
                }
            }

            return sharingBest == 1 && points[best] > 0f
                ? (BeaconFaction)best
                : BeaconFaction.None;
        }
    }
}
