namespace TidalNexus.StandaloneServer.Core
{

    public static class WeaponRange
    {

        public const float Cannon = 20f;

        public static bool InRange(
            float shooterX, float shooterY, float shooterZ,
            float targetX, float targetY, float targetZ,
            float range)
        {

            if (!(range >= 0f))
            {
                return false;
            }

            double dx = (double)shooterX - targetX;
            double dy = (double)shooterY - targetY;
            double dz = (double)shooterZ - targetZ;

            double separation = (dx * dx) + (dy * dy) + (dz * dz);

            return separation <= (double)range * range;
        }
    }
}
