using System;
using System.Collections.Generic;
using Fusion;
using TidalNexus.StandaloneServer.Data;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Services
{

    public sealed class AntiBotService
    {

        private const float AnswerWindow = 30f;

        private const float ImpossiblyFast = 1f;

        private const int WindowSize = 25;

        public sealed class Sample
        {
            public float LatencySeconds;
            public bool Correct;
        }

        public sealed class Watch
        {

            public bool Open;

            public int Answer;
            public float IssuedAt;
            public int Id;

            public bool Scheduled;

            public float NextAt;

            public readonly List<Sample> Samples = new List<Sample>();
        }

        private float _clock;
        private int _nextId;

        private readonly List<PlayerSession> _players = new List<PlayerSession>();

        private static float Interval => ServerHub.Config?.CaptchaInterval ?? 0f;

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;

            if (ServerHub.Runner == null)
            {
                return;
            }

            ServerHub.SnapshotSessions(_players);

            foreach (PlayerSession session in _players)
            {
                Expire(session);
            }

            if (Interval <= 0f)
            {
                return;
            }

            foreach (PlayerSession session in _players)
            {
                Watch watch = session.State<Watch>();

                if (watch.Open)
                {
                    continue;
                }

                if (!watch.Scheduled)
                {

                    watch.Scheduled = true;
                    watch.NextAt = _clock + UnityEngine.Random.Range(Interval, Interval * 2f);
                    continue;
                }

                if (_clock >= watch.NextAt)
                {
                    Issue(session, watch);
                }
            }
        }

        private void Issue(PlayerSession session, Watch watch)
        {
            PlayerRPC rpc = ServerHub.RpcFor(session.Player);
            if (rpc == null)
            {
                return;
            }

            int a = UnityEngine.Random.Range(100, 1000);
            int b;
            int c;

            do { b = UnityEngine.Random.Range(100, 1000); } while (b == a);
            do { c = UnityEngine.Random.Range(100, 1000); } while (c == a || c == b);

            int q = UnityEngine.Random.Range(0, 3);

            watch.Open = true;
            watch.Answer = q == 0 ? a : (q == 1 ? b : c);
            watch.IssuedAt = _clock;
            watch.Id = ++_nextId;
            watch.NextAt = _clock + Interval;

            try
            {
                rpc.RPC_RotationRequest(a, b, c, q);
            }
            catch (Exception e)
            {
                ServerLog.Warn($"could not issue captcha: {e.Message}");
                watch.Open = false;
            }
        }

        public void OnResponse(Account account, PlayerRef player, int vector)
        {
            PlayerSession session = ServerHub.SessionFor(player);
            Watch watch = session?.Peek<Watch>();

            if (watch == null || !watch.Open)
            {

                ServerLog.Info($"captcha: unsolicited answer from {account?.nickname}");
                return;
            }

            watch.Open = false;

            float latency = _clock - watch.IssuedAt;
            bool correct = vector == watch.Answer;

            Record(session.Account, watch, latency, correct);

            ServerLog.Info($"captcha #{watch.Id} {account?.nickname}: "
                + $"{(correct ? "correct" : "wrong")} in {latency:F2}s"
                + (latency < ImpossiblyFast ? " (faster than the UI allows)" : string.Empty));
        }

        private void Expire(PlayerSession session)
        {
            Watch watch = session.Peek<Watch>();
            if (watch == null || !watch.Open || _clock - watch.IssuedAt <= AnswerWindow)
            {
                return;
            }

            watch.Open = false;

            Record(session.Account, watch, AnswerWindow, correct: false);

            ServerLog.Info($"captcha: {session.Account.nickname} let it expire");
        }

        private static void Record(Account account, Watch watch, float latency, bool correct)
        {
            if (account == null)
            {
                return;
            }

            List<Sample> samples = watch.Samples;
            samples.Add(new Sample { LatencySeconds = latency, Correct = correct });

            if (samples.Count > WindowSize * 2)
            {
                samples.RemoveRange(0, samples.Count - WindowSize);
            }

            if (samples.Count >= WindowSize)
            {
                Score(account, samples);
            }
        }

        private static void Score(Account account, List<Sample> samples)
        {
            int correct = 0;
            float total = 0f;

            foreach (Sample s in samples)
            {
                if (s.Correct)
                {
                    correct++;
                }

                total += s.LatencySeconds;
            }

            float mean = total / samples.Count;

            float variance = 0f;
            foreach (Sample s in samples)
            {
                float d = s.LatencySeconds - mean;
                variance += d * d;
            }

            float deviation = Mathf.Sqrt(variance / samples.Count);
            float accuracy = (float)correct / samples.Count;

            float skew = 0f;
            if (deviation > 0.0001f)
            {
                foreach (Sample s in samples)
                {
                    float d = (s.LatencySeconds - mean) / deviation;
                    skew += d * d * d;
                }

                skew /= samples.Count;
            }

            ServerLog.Info($"captcha profile {account.nickname}: n={samples.Count} "
                + $"mean={mean:F2}s sd={deviation:F2} skew={skew:F2} "
                + $"accuracy={accuracy:P0}");
        }
    }
}
