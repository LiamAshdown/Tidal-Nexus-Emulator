using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    internal abstract class EventMode
    {
        protected EventMode(EventService events)
        {
            Events = events;
        }

        protected EventService Events { get; }

        public abstract EventService.Kind Kind { get; }

        public abstract string Label { get; }

        protected abstract int Sector { get; }

        public virtual float Duration => 1800f;

        public abstract GameObject Prefab(ServerPrefabs prefabs);

        public virtual Vector3 Place() => EventService.AreaCentre(Sector);

        public abstract void Bind(NetworkObject spawned);

        public abstract void PublishTimer(float timeLeft);

        public abstract void Begin();

        public virtual void Advance(float deltaTime, float timeLeft)
        {
        }

        public virtual void Ending()
        {
        }

        public virtual bool Join(PlayerRef player, Account account) => false;

        public virtual void NoteKill(Account killer, Account victim)
        {
            Events.Credit(killer, kills: 1, points: 5);
            Events.Credit(victim, deaths: 1);
        }

        public virtual void NoteDamage(Account attacker, NPCBehaviour target, int amount)
        {
        }

        public virtual void NoteNpcKill(Account killer, NPCBehaviour target)
        {
        }

        public void Report(List<EventService.Contribution> scores)
        {
            foreach (KeyValuePair<PlayerRef, Account> kv in ServerHub.Online)
            {
                SendReport(kv.Key, scores);
            }
        }

        protected abstract void SendReport(PlayerRef player,
            List<EventService.Contribution> scores);

        public virtual void Award(Dictionary<string, EventService.Contribution> scores)
        {
            int perPoint = ServerHub.Config?.EventCreditsPerPoint ?? 100;

            foreach (KeyValuePair<string, EventService.Contribution> kv in scores)
            {
                Account account = AccountStore.Find(kv.Key);
                if (account == null || kv.Value.Points <= 0)
                {
                    continue;
                }

                account.credits += (long)kv.Value.Points * perPoint;
                AccountStore.MarkDirty(account);

                PlayerRef p = ServerHub.RefFor(account);
                if (p != PlayerRef.None)
                {
                    ServerHub.Accounts?.PushWallet(p, account);
                }
            }
        }

        protected void CouldNotSet(string member, Exception e) =>
            ServerLog.Warn($"could not set {member} on the {Kind} event: {e.Message}");

        private static readonly Variant[] Variants =
        {
            new Variant(EventService.Kind.Beacon, e => new BeaconMode(e),
                "beacon", "bz", "bzbeacon"),
            new Variant(EventService.Kind.Kraken, e => new KrakenMode(e),
                "kraken"),
            new Variant(EventService.Kind.Royale, e => new RoyaleMode(e),
                "royale", "br"),
        };

        public static EventMode Create(EventService events, EventService.Kind kind)
        {
            foreach (Variant variant in Variants)
            {
                if (variant.Kind == kind)
                {
                    return variant.Build(events);
                }
            }

            return null;
        }

        public static EventService.Kind KindNamed(string name)
        {
            string wanted = (name ?? string.Empty).ToLowerInvariant();

            foreach (Variant variant in Variants)
            {
                foreach (string candidate in variant.Names)
                {
                    if (candidate == wanted)
                    {
                        return variant.Kind;
                    }
                }
            }

            return EventService.Kind.None;
        }

        private readonly struct Variant
        {
            public Variant(EventService.Kind kind, Func<EventService, EventMode> build,
                params string[] names)
            {
                Kind = kind;
                Build = build;
                Names = names;
            }

            public EventService.Kind Kind { get; }

            public Func<EventService, EventMode> Build { get; }

            public string[] Names { get; }
        }
    }
}
