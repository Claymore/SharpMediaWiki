﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Claymore.SharpMediaWiki
{
    public static class WikiCache
    {
        public static void Login(Wiki wiki, string username, string password)
        {
            if (!WikiCache.LoadCookies(wiki))
            {
                wiki.Login(username, password);
                WikiCache.CacheCookies(wiki);
            }
            else
            {
                wiki.Login();
                if (!wiki.IsBot)
                {
                    wiki.Logout();
                    wiki.Login(username, password);
                    WikiCache.CacheCookies(wiki);
                }
            }
        }

        public static void Login(Wiki wiki, string username, string password, string fileName)
        {
            if (!WikiCache.LoadCookies(wiki, fileName))
            {
                wiki.Login(username, password);
                WikiCache.CacheCookies(wiki, fileName);
            }
            else
            {
                wiki.Login();
                if (!wiki.IsBot)
                {
                    wiki.Logout();
                    wiki.Login(username, password);
                    WikiCache.CacheCookies(wiki, fileName);
                }
            }
        }

        public static void CacheCookies(Wiki wiki, string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Create))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
            {
                byte[] data = wiki.CookiesToArray();
                gs.Write(data, 0, data.Length);
            }
        }

        public static void CacheCookies(Wiki wiki)
        {
            Directory.CreateDirectory("Cache");
            string filename = @"Cache\cookie.jar";
            CacheCookies(wiki, filename);
        }

        public static bool LoadCookies(Wiki wiki)
        {
            return LoadCookies(wiki, @"Cache\cookie.jar");
        }

        public static bool LoadCookies(Wiki wiki, string fileName)
        {
            if (!File.Exists(fileName))
            {
                return false;
            }
            using (FileStream fs = new FileStream(fileName, FileMode.Open))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Decompress))
            using (BinaryReader sr = new BinaryReader(gs))
            {
                List<byte> data = new List<byte>();
                int b;
                while ((b = sr.BaseStream.ReadByte()) != -1)
                {
                    data.Add((byte)b);
                }
                wiki.LoadCookies(data.ToArray());
            }
            return true;
        }

        public static bool LoadNamespaces(Wiki wiki)
        {
            return LoadNamespaces(wiki, @"Cache\namespaces.dat");
        }

        public static void CacheNamespaces(Wiki wiki)
        {
            CacheNamespaces(wiki, @"Cache\namespaces.dat");
        }

        public static bool LoadNamespaces(Wiki wiki, string filename)
        {
            if (!File.Exists(filename))
            {
                return false;
            }
            using (FileStream fs = new FileStream(filename, FileMode.Open))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Decompress))
            using (BinaryReader sr = new BinaryReader(gs))
            {
                List<byte> data = new List<byte>();
                int b;
                while ((b = sr.BaseStream.ReadByte()) != -1)
                {
                    data.Add((byte)b);
                }
                wiki.LoadNamespaces(data);
            }
            return true;
        }

        public static void CacheNamespaces(Wiki wiki, string filename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            using (GZipStream gs = new GZipStream(fs, CompressionMode.Compress))
            {
                byte[] data = wiki.NamespacesToArray();
                gs.Write(data, 0, data.Length);
            }
        }

        public static string EscapePath(string path)
        {
            Regex charsRE = new Regex(@"[:/\*\?<>\|\n]");
            return charsRE.Replace(path, "_").Replace('"', '_').Replace('\\', '_');
        }
    }
}
