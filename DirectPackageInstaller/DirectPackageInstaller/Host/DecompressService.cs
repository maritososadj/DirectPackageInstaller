﻿using DirectPackageInstaller.Compression;
using HttpServerLite;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace DirectPackageInstaller.Host
{
    public class DecompressService
    {
        const long MaxSkipBufferSize = 1024 * 1024 * 100;

        private readonly Dictionary<string, int> Instances = new Dictionary<string, int>();

        public static readonly Dictionary<string, string?> EntryMap = new Dictionary<string, string?>();
        public static readonly Dictionary<string, (string? Entry, string? Url)> TaskCache = new Dictionary<string, (string? Entry, string? Url)>();

        internal readonly SharpComp Decompressor = new SharpComp();

        internal Dictionary<string, DecompressTaskInfo> Tasks => Decompressor.Tasks;

        public async Task Unrar(HttpContext Context, NameValueCollection Query, bool FromPS4)
        {
            if (!Query.AllKeys.Contains("url") && !Query.AllKeys.Contains("id"))
                return;

            string? Url = null;
            string? Entry = "";

            if (Query.AllKeys.Contains("entry"))
                Entry = Query["entry"];

            if (Query.AllKeys.Contains("url"))
                Url = Query["url"];

            if (Query.AllKeys.Contains("id") && TaskCache.ContainsKey(Query["id"]))
            {
                var Task = TaskCache[Query["id"] ?? throw new NullReferenceException()];
                Url = Task.Url;
                Entry = Task.Entry;
            }

            if (!EntryMap.ContainsKey(Url))
            {
                if (!Tasks.ContainsKey(EntryMap[Url]))
                    return;
                
                EntryMap[Url] = Tasks[EntryMap[Url]].EntryName;
            }

            await Decompress(Context, Url, Entry, FromPS4);
        }

        public async Task Un7z(HttpContext Context, NameValueCollection Query, bool FromPS4)
        {
            if (!Query.AllKeys.Contains("url") && !Query.AllKeys.Contains("id"))
                return;

            string? Url = null;
            string? Entry = "";

            if (Query.AllKeys.Contains("entry"))
                Entry = Query["entry"];

            if (Query.AllKeys.Contains("url"))
                Url = Query["url"];

            if (Query.AllKeys.Contains("id") && TaskCache.ContainsKey(Query["id"]!))
            {
                var Task = TaskCache[Query["id"]!];
                Url = Task.Url;
                Entry = Task.Entry;
            }

            if (!EntryMap.ContainsKey(Url))
            {
                if (!Tasks.ContainsKey(EntryMap[Url]))
                    return;
                
                EntryMap[Url] = Tasks[EntryMap[Url]].EntryName;
            }

            await Decompress(Context, Url, Entry, FromPS4);
        }

        async Task Decompress(HttpContext Context, string Url, string? Entry, bool FromPS4)
        {
            HttpRange? Range = null;
            bool Partial = Context.Request.HeaderExists("Range", true);
            if (Partial)
                Range = new HttpRange(Context.Request.Headers["Range"]);

            var InstanceID = Url + Entry;

            DecompressTaskInfo TaskInfo = default;
            bool SeekRequest = false;

            if (EntryMap.ContainsKey(Url))
            {
                TaskInfo = Tasks[EntryMap[Url]];
                SeekRequest = (Range?.Begin ?? 0) > TaskInfo.SafeTotalDecompressed + MaxSkipBufferSize;
            }

            if (TaskInfo.Failed && TaskInfo.Error != null)
            {
                if (System.Diagnostics.Debugger.IsAttached)
                    System.Diagnostics.Debugger.Break();
                System.IO.File.WriteAllText("decompress.log", $"{Tasks[EntryMap[Url]].Error}");
                Tasks.Remove(EntryMap[Url]);
            }

            if (FromPS4)
            {
                if (!Instances.ContainsKey(InstanceID))
                    Instances[InstanceID] = 0;

                Instances[InstanceID]++;
            }

            if (FromPS4 && SeekRequest && Instances[InstanceID] > 1)
            {
                try
                {

                    Context.Response.StatusCode = 429;
                    Context.Response.Headers["Connection"] = "close";
                    Context.Response.Headers["Retry-After"] = (60 * 5).ToString();
                    Context.Response.Send(true);
                }
                catch { }
                finally {
                    if (FromPS4)
                        Instances[InstanceID]--;
                }
                return;
            }

            var RespData = TaskInfo.Content();

            try
            {
                Context.Response.Headers["Connection"] = "Keep-Alive";
                Context.Response.Headers["Accept-Ranges"] = "none";
                Context.Response.Headers["Content-Type"] = "application/octet-stream";

                if (Partial)
                {
                    Context.Response.ContentLength = Range?.Length ?? TaskInfo.TotalSize - Range?.Begin ?? TaskInfo.TotalSize;
                    Context.Response.Headers["Content-Range"] = $"bytes {Range?.Begin ?? 0}-{Range?.End ?? TaskInfo.TotalSize}/{TaskInfo.TotalSize}";

                    RespData = new VirtualStream(RespData, Range?.Begin ?? 0, Context.Response.ContentLength.Value);

                }
                else
                    RespData = new VirtualStream(RespData, 0, TaskInfo.TotalSize);

                ((VirtualStream)RespData).ForceAmount = true;

                await Context.Response.SendAsync(Context.Response.ContentLength ?? TaskInfo.TotalSize, RespData);
            }
            finally
            {
                RespData?.Close();

                if (FromPS4)
                    Instances[InstanceID]--;
            }
        }
    }
}
