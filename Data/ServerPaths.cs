using System;
using System.IO;
using UnityEngine;

namespace TidalNexus.StandaloneServer.Data
{

    public static class ServerPaths
    {
        private static string _root;

        public static string DataRoot => _root ??= Resolve();

        private static string Resolve()
        {
            string configured = Environment.GetEnvironmentVariable("TIDALNEXUS_DATA");
            string root = string.IsNullOrEmpty(configured)
                ? Path.Combine(Application.dataPath, "..", "serverdata")
                : configured;

            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception e)
            {
                ServerLog.Error($"could not create the data directory {root}: {e.Message}");
            }

            return root;
        }
    }
}
