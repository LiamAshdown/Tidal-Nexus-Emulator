using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class NpcState
    {
        public NpcState(NPCBehaviour npc)
        {
            Npc = npc;
            NextAttackAt = 0f;

            Home = npc != null ? npc.transform.position : Vector3.zero;
        }

        public NPCBehaviour Npc { get; }

        public Vector3 Home { get; }

        public Player Target;

        public float NextAttackAt;

        public float PositionClock;

        public float[] LoopSegments;

        public float LoopLength;

        public bool Alive => Npc != null && WorldLookup.IsAlive(Npc.health);

        public Vector3 Position => Npc != null ? Npc.transform.position : Vector3.zero;
    }
}
