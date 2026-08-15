using Fusion;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public static class CollectableSpawn
    {

        public static NetworkObject At(GameObject prefab, Vector3 at, int npcIndex = -1)
        {
            if (prefab == null || ServerHub.Runner == null)
            {
                return null;
            }

            return ServerHub.Runner.Spawn(
                prefab,
                at,
                Quaternion.identity,
                null,
                (runner, obj) =>
                {
                    var collectable = obj.GetComponent<CollectableObject>();
                    if (collectable == null)
                    {
                        return;
                    }

                    collectable.position = at;

                    if (npcIndex >= 0)
                    {
                        collectable.npcIndex = npcIndex;
                    }
                });
        }
    }
}
