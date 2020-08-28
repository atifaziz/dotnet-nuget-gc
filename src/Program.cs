﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Options;

namespace NugetCacheCleaner
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Wain(args);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetBaseException().Message);
                return 0xff;
            }
        }

        static void Wain(IEnumerable<string> args)
        {
            var force = false;
            var showHelp = false;
            var minDays = TimeSpan.FromDays(30);

            var options = new OptionSet {
                {"f|force", "Performs the actual clean-up. Default is to do a dry-run and report the clean-up that would be done.", v => force = v != null},
                {"m|min-days=", "Number of days a package must not be used in order to be purged from the cache. Defaults to 30.", v => minDays = ParseDays(v)},
                { "?|h|help", "show this message and exit", v => showHelp = v != null },
            };

            var extra = options.Parse(args);

            if (showHelp)
            {
                ShowHelp(options);
                return;
            }

            var totalDeleted = CleanCache(force, minDays);
            var mbDeleted = (totalDeleted / 1024d / 1024d).ToString("0");

            if (force)
            {
                Console.WriteLine($"Done! Deleted {mbDeleted} MB.");
            }
            else
            {
                Console.WriteLine($"{mbDeleted} MB worth of packages are older than {minDays.TotalDays:N0} days and would be deleted.");
                Console.WriteLine("Re-run with -f or --force flag to really delete.");
            }
        }

        private static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("usage: dotnet nuget-gc [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions (Console.Out);
        }

        private static TimeSpan ParseDays(string text)
        {
            if (!int.TryParse(text, out var days))
                throw new FormatException($"'{text}' isn't a valid integer");

            return TimeSpan.FromDays(days);
        }

        private static long CleanCache(bool force, TimeSpan minDays)
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var nugetCachePath = Path.Join(userProfilePath, ".nuget", "packages");
            var nugetCache = new DirectoryInfo(nugetCachePath);
            var totalDeleted = 0L;
            foreach (var folder in nugetCache.GetDirectories())
            {
                foreach (var versionFolder in folder.GetDirectories())
                {
                    var files = versionFolder.GetFiles("*.*", SearchOption.AllDirectories);
                    var size = files.Sum(f => f.Length);
                    var lastAccessed = DateTime.Now - files.Max(f => f.LastAccessTime);
                    if (lastAccessed > minDays)
                    {
                        Console.WriteLine($"{versionFolder.FullName} last accessed {Math.Floor(lastAccessed.TotalDays)} days ago");
                        try
                        {
                            Delete(versionFolder, force, withLockCheck: true);
                            totalDeleted += size;
                        }
                        catch { }
                    }
                }
                if (folder.GetDirectories().Length == 0)
                    Delete(folder, force, withLockCheck: false);
            }

            return totalDeleted;
        }

        private static void Delete(DirectoryInfo dir, bool force, bool withLockCheck)
        {
            if (!force)
                return;

            if (withLockCheck) // This may only be good enough for Windows
            {
                var tempPath = Path.Join(dir.Parent.FullName, "_" + dir.Name);
                dir.MoveTo(tempPath); // Attempt to rename before deleting
                Directory.Delete(tempPath, recursive: true);
            }
            else
            {
                dir.Delete(recursive: true);
            }
        }
    }
}