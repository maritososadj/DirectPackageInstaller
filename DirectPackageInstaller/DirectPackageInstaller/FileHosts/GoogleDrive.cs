﻿using System;
using System.Collections.Generic;
using System.Linq;
using DirectPackageInstaller.Views;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using DirectPackageInstaller.UIBase;

namespace DirectPackageInstaller.FileHosts
{
    class GoogleDrive : FileHostBase
    {
        public override string HostName => "GoogleDrive";
        public override bool Limited => false;
        
        static bool WaitingCookies = false;
        static bool CookieAsked = false;
        
        static List<Cookie> UserCookies = new List<Cookie>();

        public override DownloadInfo GetDownloadInfo(string URL)
        {
            if (!IsValidUrl(URL))
                throw new Exception("Invalid Url");

            List<Cookie> Cookies = new List<Cookie>();
            Cookies.AddRange(UserCookies);

            var DownloadPageUri = $"https://drive.google.com/u/0/uc?id={GetFileID(URL)}&export=download";

            var DownloadUri = $"{DownloadPageUri}&confirm=t";

            var Headers = Head(DownloadUri, Cookies.ToArray());
            
            if (Headers == null || Headers.AllKeys.Contains("x-auto-login"))
            {
                var OldCount = Cookies.Count;
                UserCookies = CookieManagerView.GetUserCookies("google.com");

                bool CookiesWaited = false;
                while (WaitingCookies)
                {
                    CookiesWaited = true;
                    Task.Delay(100).Wait();
                }

                if (OldCount == UserCookies.Count && (UserCookies.Count == 0 || !CookieAsked) && !CookiesWaited)
                {
                    CookieAsked = true;
                    WaitingCookies = true;

                    if (App.IsSingleView)
                    {
                        var CManager = Extensions.CreateInstance<CookieManagerView>(null);
                        SingleView.CallView(CManager, true).Wait();
                    }
                    else
                    {
                        var CManager = DialogWindow.CreateInstance<CookieManager>();
                        CManager.ShowDialogSync(MainWindow.Instance);
                    }

                    WaitingCookies = false;
                    UserCookies = CookieManagerView.GetUserCookies("google.com");
                    return GetDownloadInfo(URL);
                }
                
                if (CookiesWaited || OldCount != UserCookies.Count)
                    return GetDownloadInfo(URL);
                
                throw new Exception();
            }

            if (Cookies.Any())
            {
                CookieAsked = true;
            }

            return new DownloadInfo()
            {
                Headers = Cookies.Count == 0 ? null : new List<(string Key, string Value)>()
                {
                    ("referer", "https://drive.google.com/"),
                    ("user-agent", UserAgent)
                },
                Cookies = Cookies.Count == 0 ? null : Cookies.ToArray(),
                Url = DownloadUri
            };
        }

        public override bool IsValidUrl(string URL)
        {
            return URL.Contains("drive.google.com") && GetFileID(URL) != null;
        }

        string GetFileID(string URL)
        {
            //https://drive.google.com/file/d/161nRA0mXrCfrSWW11rfg9g0nmCzaUR99/view
            //https://drive.google.com/u/0/uc?id=161nRA0mXrCfrSWW11rfg9g0nmCzaUR99&export=download
            //https://drive.google.com/open?id=161nRA0mXrCfrSWW11rfg9g0nmCzaUR99

            if (URL.Contains("id=") && URL.Contains("&export="))
                return URL.Substring("id=", "&export=");            

            if (URL.Contains("id="))
                return URL.Substring("id=");

            if (URL.Contains("/file/d/"))
                return URL.Substring("/file/d/", "/");

            return null;
        }
    }
}
