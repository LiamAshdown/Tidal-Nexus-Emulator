using System.Linq;
using Fusion;
using UnityEngine;

namespace TidalNexus.StandaloneServer
{

    public sealed class AutoPlay : MonoBehaviour
    {

        private const float ArriveDistance = 12f;

        private const float EngageDistance = 35f;

        private const float RetargetInterval = 6f;

        private float nextAction;
        private NPCBehaviour target;
        private bool announced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("TN_AUTOPLAY")))
            {
                return;
            }

            var host = new GameObject("AutoPlay");
            host.AddComponent<AutoPlay>();
            DontDestroyOnLoad(host);
            Debug.Log("[AutoPlay] enabled");
        }

        private void Update()
        {
            Player me = NetworkManager.Instance != null
                ? NetworkManager.Instance.localPlayer
                : null;

            if (me == null || MovementTarget.Instance == null)
            {
                return;
            }

            if (!announced)
            {
                announced = true;
                Debug.Log($"[AutoPlay] in world at {me.transform.position}");
            }

            if (Time.time < nextAction)
            {
                return;
            }

            nextAction = Time.time + RetargetInterval;

            NPCBehaviour[] npcs = Object.FindObjectsByType<NPCBehaviour>(FindObjectsSortMode.None);
            if (npcs.Length == 0)
            {
                Debug.Log("[AutoPlay] no NPCs replicated to this client yet");
                Wander(me);
                return;
            }

            Vector3 here = me.transform.position;
            target = npcs
                .Where(n => n != null && n.Object != null && n.Object.IsValid)
                .OrderBy(n => Vector3.Distance(here, n.transform.position))
                .FirstOrDefault();

            if (target == null)
            {
                Wander(me);
                return;
            }

            float distance = Vector3.Distance(here, target.transform.position);
            Debug.Log($"[AutoPlay] nearest NPC {target.name} at {distance:F1}m ({npcs.Length} visible)");

            if (distance > EngageDistance)
            {

                MovementTarget.Instance.SetPosition(
                    target.transform.position, isMap: false, overrideInput: true);
                Debug.Log($"[AutoPlay] moving to {target.transform.position}");
                return;
            }

            Attack(me);
        }

        private void Attack(Player me)
        {
            if (target == null || me.rpc == null)
            {
                return;
            }

            Vector3 p = target.transform.position;
            me.rpc.RPC_SendTarget(target.Object.Id, new Vector2(p.x, p.z));
            Debug.Log($"[AutoPlay] attacking {target.name} (id {target.Object.Id})");
        }

        private void Wander(Player me)
        {
            Vector2 offset = Random.insideUnitCircle * 60f;
            Vector3 destination = me.transform.position + new Vector3(offset.x, 0f, offset.y);

            MovementTarget.Instance.SetPosition(destination, isMap: false, overrideInput: true);
            Debug.Log($"[AutoPlay] wandering to {destination}");
        }
    }
}
