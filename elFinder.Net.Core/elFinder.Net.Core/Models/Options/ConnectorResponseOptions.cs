﻿using elFinder.Net.Core.Models.Command;
using System.Collections.Generic;
using System.Net.Mime;

namespace elFinder.Net.Core.Models.Options
{
    public class ConnectorResponseOptions
    {
        public ConnectorResponseOptions(PathInfo pathInfo, IEnumerable<string> disabled = null, char separator = default)
        {
            this.disabled = disabled ?? ConnectorCommand.NotSupportedUICommands;
            this.separator = separator == default ? pathInfo.Volume.DirectorySeparatorChar : separator;
            path = pathInfo.Volume.Name;
            if (pathInfo.Path != string.Empty)
            {
                path += this.separator + pathInfo.Path.Replace(pathInfo.Volume.DirectorySeparatorChar, this.separator);
            }
            url = pathInfo.Volume.Url ?? string.Empty;
            tmbUrl = pathInfo.Volume.ThumbnailUrl ?? string.Empty;
            var zipMime = MediaTypeNames.Application.Zip;
            archivers = new ArchiveOptions
            {
                create = new[] { zipMime },
                extract = new[] { zipMime },
                createext = new Dictionary<string, string>
                {
                    { zipMime , FileExtensions.Zip }
                }
            };
        }

        public ArchiveOptions archivers { get; set; }

        public IEnumerable<string> disabled { get; }

        public byte copyOverwrite => 1;

        public string path { get; set; }

        public char separator { get; set; }

        public string tmbUrl { get; set; }

        public string trashHash => string.Empty;

        public int uploadMaxConn => -1;

        public string uploadMaxSize { get; set; }

        public string url { get; set; }
    }
}
