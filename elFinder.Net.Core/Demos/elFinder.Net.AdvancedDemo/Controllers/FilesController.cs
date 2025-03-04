﻿using elFinder.Net.AdvancedDemo.Models;
using elFinder.Net.AdvancedDemo.Models.Responses;
using elFinder.Net.AspNetCore.Extensions;
using elFinder.Net.AspNetCore.Helper;
using elFinder.Net.Core;
using elFinder.Net.Core.Models.Response;
using elFinder.Net.Core.Models.Result;
using elFinder.Net.Drivers.FileSystem.Helpers;
using elFinder.Net.Plugins.FileSystemQuotaManagement;
using elFinder.Net.Plugins.FileSystemQuotaManagement.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace elFinder.Net.AdvancedDemo.Controllers
{
    [Route("api/files")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
    public class FilesController : Controller
    {
        private readonly IConnector _connector;
        private readonly IDriver _driver;
        private readonly IStorageManager _storageManager;
        private readonly DataContext _dataContext;

        public FilesController(IConnector connector,
            IDriver driver,
            IStorageManager storageManager,
            DataContext dataContext)
        {
            _connector = connector;
            _driver = driver;
            _storageManager = storageManager;
            _dataContext = dataContext;
        }

        [Route("connector")]
        public async Task<IActionResult> Connector()
        {
            var ccTokenSource = ConnectorHelper.RegisterCcTokenSource(HttpContext);
            var (volume, quota) = await SetupConnectorAsync(ccTokenSource.Token);
            var cmd = ConnectorHelper.ParseCommand(Request);
            var conResult = await _connector.ProcessAsync(cmd, ccTokenSource);
            CustomizeResponse(conResult, volume, quota);
            var actionResult = conResult.ToActionResult(HttpContext);
            return actionResult;
        }

        [Route("thumb/{target}")]
        public async Task<IActionResult> Thumb(string target)
        {
            await SetupConnectorAsync(HttpContext.RequestAborted);
            var thumb = await _connector.GetThumbAsync(target, HttpContext.RequestAborted);
            var actionResult = ConnectorHelper.GetThumbResult(thumb);
            return actionResult;
        }

        private void CustomizeResponse(ConnectorResult connectorResult, IVolume volume, long quota)
        {
            var storageCache = _storageManager.GetOrCreateDirectoryStorage(volume.RootDirectory,
                    (dir) => volume.Driver.CreateDirectory(dir, volume).GetPhysicalStorageUsageAsync(HttpContext.RequestAborted));

            if (connectorResult.Response is InitResponse initResp)
            {
                connectorResult.Response = new ApplicationInitResponse(initResp)
                {
                    quota = quota,
                    usage = storageCache.Storage
                };
            }
            else if (connectorResult.Response is OpenResponse openResp)
            {
                connectorResult.Response = new ApplicationOpenResponse(openResp)
                {
                    quota = quota,
                    usage = storageCache.Storage
                };
            }
        }

        private async Task<(IVolume Volume, long Quota)> SetupConnectorAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var userId = int.Parse(User.Identity.Name);
            var user = _dataContext.Users.Single(o => o.Id == userId);
            var volumePath = user.VolumePath;
            var quota = user.QuotaInBytes;

            // Quota management: The 2 line belows is setup once per request
            var quotaOptions = new QuotaOptions() { Enabled = true };
            _connector.PluginManager.Features[typeof(QuotaOptions)] = quotaOptions;

            // Volume initialization
            var volume = new Volume(_driver, Startup.MapPath($"~/upload/{volumePath}"), $"/upload/{volumePath}/", $"/api/files/thumb/")
            {
                Name = "My volume",
                ThumbnailDirectory = PathHelper.GetFullPath($"./thumb/{volumePath}")
            };

            _connector.AddVolume(volume);
            await _driver.SetupVolumeAsync(volume, cancellationToken);

            // Events
            _driver.OnAfterUpload += (sender, args) =>
            {
                Console.WriteLine($"Uploaded to: {args.File.FullName}");
            };

            // Quota management: This is set up per volume. Use VolumeId as key.
            // The plugin support quota management on Volume (root) level only. It means that you can not set quota for directories.
            quotaOptions.Quotas[volume.VolumeId] = new VolumeQuota
            {
                VolumeId = volume.VolumeId,
                MaxStorageSizeInMb = quota / Math.Pow(1024, 2),
                //MaxStorageSizeInKb = quota / 1024,
                //MaxStorageSize = quota,
            };

            #region Access Control Management
            var limitedFolder = $"{volume.RootDirectory}{volume.DirectorySeparatorChar}limited";
            var haloFile = $"{volume.RootDirectory}{volume.DirectorySeparatorChar}halo.txt";
            var adminArea = $"{volume.RootDirectory}{volume.DirectorySeparatorChar}admin-area";

            volume.ObjectAttributes = new List<FilteredObjectAttribute>()
            {
                // You can implement your own logic to modify Physical File attributes to maintain the Access Control attributes
                // even if the files are moved
                new FilteredObjectAttribute()
                {
                    Expression = $"file.attributes.readonly", // Example only
                    ObjectFilter = (obj) => (int)obj.Attributes != -1 && obj.Attributes.HasFlag(FileAttributes.ReadOnly),
                    Write = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"file.attributes.hidden", // Example only
                    ObjectFilter = (obj) => (int)obj.Attributes != -1 && obj.Attributes.HasFlag(FileAttributes.Hidden),
                    Visible = false
                },

                // Recommended: If the parent is limited, then its children should be too.
                new FilteredObjectAttribute()
                {
                    Expression = $"dir.fullname = '{adminArea}'//children", // Example only
                    DirectoryFilter = (dir) => dir.FullName == adminArea,
                    ObjectFilter = (obj) => obj.IsChildOfAsync(adminArea).Result,
                    Access = false
                },

                // More examples
                new FilteredObjectAttribute()
                {
                    Expression = $"obj.fullname = '{limitedFolder}'", // Example only
                    ObjectFilter = (obj) => obj.FullName == limitedFolder,
                    Locked = true, Read = false, Write = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"obj.fullname = '{limitedFolder}'", // Example only
                    ObjectFilter = (obj) => obj.FullName == limitedFolder,
                    Locked = true, Read = false, Write = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"obj.fullname = '{haloFile}'", // Example only
                    ObjectFilter = (obj) => obj.FullName == haloFile,
                    Locked = true, Write = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"file.name.startsWith = 'secrets_'", // Example only
                    FileFilter = (file) => file.Name.StartsWith("secrets_"),
                    Locked = true, Write = false, Visible = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"file.mime = 'somemime-type'", // Example only
                    FileFilter = (file) => file.MimeType == "somemime-type",
                    Locked = true, Write = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"file.ext = 'exe'", // Example only
                    FileFilter = (file) => file.Extension == ".exe",
                    Locked = true, Write = false, ShowOnly = true, Read = false
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"dir.parent.name = 'locked'", // Example only
                    DirectoryFilter = (dir) => dir.Parent.Name == "locked",
                    Locked = true
                },
                new FilteredObjectAttribute()
                {
                    Expression = $"dir.name = 'access-denied'", // Example only
                    DirectoryFilter = (dir) => dir.Name == "access-denied",
                    Access = false
                },
                //new FilteredObjectAttribute()
                //{
                //    Expression = $"obj.isroot", // Example only
                //    ObjectFilter = (obj) => volume.IsRoot(obj),
                //    Access = true, Visible = true, Read = true, Write = true, Locked = false, ShowOnly = false
                //},
            };

            // Or if you want to restrict all by default
            //volume.DefaultObjectAttribute = new ObjectAttribute
            //{
            //    Visible = false,
            //    Access = false
            //};
            #endregion

            return (volume, quota);
        }
    }
}
