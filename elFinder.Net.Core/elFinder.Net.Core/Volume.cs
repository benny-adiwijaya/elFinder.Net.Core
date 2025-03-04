﻿using elFinder.Net.Core.Extensions;
using elFinder.Net.Core.Services;
using elFinder.Net.Core.Services.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace elFinder.Net.Core
{
    public interface IVolume
    {
        string VolumeId { get; set; }
        string Name { get; set; }
        string Url { get; }
        string RootDirectory { get; }
        string StartDirectory { get; set; }
        string ThumbnailUrl { get; set; }
        string ThumbnailDirectory { get; set; }
        int ThumbnailSize { get; set; }
        char DirectorySeparatorChar { get; set; }
        bool UploadOverwrite { get; set; }
        bool CopyOverwrite { get; set; }
        bool IsReadOnly { get; set; }
        bool IsLocked { get; set; }
        bool IsShowOnly { get; set; }
        /// <summary>
        /// Get or sets maximum upload file size. This size is per files in bytes.
        /// Note: you still to configure maxupload limits in web.config for whole application
        /// </summary>
        double? MaxUploadSize { get; set; }
        double? MaxUploadSizeInKb { get; set; }
        double? MaxUploadSizeInMb { get; set; }
        IDriver Driver { get; }
        /// <summary>
        /// List of object filters used for permissions, access control.
        /// Last filter will override all previous.
        /// </summary>
        IEnumerable<FilteredObjectAttribute> ObjectAttributes { get; set; }
        /// <summary>
        /// Default attribute for files/directories if not any specific object attribute detected. 
        /// </summary>
        ObjectAttribute DefaultObjectAttribute { get; set; }
        Task<string> GenerateThumbHashAsync(IFile originalImage, IPathParser pathParser, IPictureEditor pictureEditor, CancellationToken cancellationToken = default);
        Task<string> GenerateThumbPathAsync(IFile originalImage, IPictureEditor pictureEditor, CancellationToken cancellationToken = default);
        Task<string> GenerateThumbPathAsync(IDirectory directory, CancellationToken cancellationToken = default);
        bool IsRoot(IFileSystem fileSystem);
        bool Own(IFileSystem fileSystem);
        bool Own(string fullPath);
        bool IsRoot(string fullPath);
        string GetRelativePath(IFileSystem fileSystem);
        string GetRelativePath(string fullPath);
        string GetPathUrl(string fullPath);
    }

    public class Volume : IVolume
    {
        public const string VolumePrefix = "v";
        public const string HashSeparator = "_";

        public Volume(IDriver driver,
            string rootDirectory, string url, string thumbUrl,
            char directorySeparatorChar = default)
        {
            if (rootDirectory == null)
                throw new ArgumentNullException(nameof(rootDirectory));
            if (url == null)
                throw new ArgumentNullException(nameof(url));

            Driver = driver;
            RootDirectory = rootDirectory;
            Url = url;

            if (!string.IsNullOrEmpty(thumbUrl))
            {
                ThumbnailUrl = thumbUrl;
            }

            DirectorySeparatorChar = directorySeparatorChar == default ? Path.DirectorySeparatorChar : directorySeparatorChar;
            ThumbnailDirectory = $"{Path.GetTempPath()}{DirectorySeparatorChar}.{nameof(elFinder)}tmb";
            IsLocked = false;
            Name = Path.GetFileNameWithoutExtension(rootDirectory);
            UploadOverwrite = true;
            CopyOverwrite = true;
            ThumbnailSize = 48;
        }

        public virtual string VolumeId { get; set; }
        public virtual string Name { get; set; }
        public virtual string Url { get; }
        public virtual string RootDirectory { get; }

        private string _startDirectory;
        public virtual string StartDirectory
        {
            get => _startDirectory; set
            {
                if (_startDirectory != null && !IsRoot(_startDirectory)
                    && !_startDirectory.StartsWith(RootDirectory + DirectorySeparatorChar))
                    throw new InvalidOperationException("Invalid start directory");
                _startDirectory = value;
            }
        }
        public virtual string ThumbnailUrl { get; set; }
        public virtual string ThumbnailDirectory { get; set; }
        public virtual int ThumbnailSize { get; set; }
        public virtual char DirectorySeparatorChar { get; set; }
        public virtual bool UploadOverwrite { get; set; }
        public virtual bool CopyOverwrite { get; set; }
        public virtual bool IsShowOnly { get; set; }
        public virtual bool IsReadOnly { get; set; }
        public virtual bool IsLocked { get; set; }
        public virtual IDriver Driver { get; }
        public virtual IEnumerable<FilteredObjectAttribute> ObjectAttributes { get; set; }

        private ObjectAttribute _defaultAttribute = ObjectAttribute.Default;
        public virtual ObjectAttribute DefaultObjectAttribute
        {
            get => _defaultAttribute; set
            {
                if (value == null) throw new ArgumentNullException(nameof(DefaultObjectAttribute));
                _defaultAttribute = value;
            }
        }

        public virtual double? MaxUploadSize { get; set; }

        public virtual double? MaxUploadSizeInKb
        {
            get { return MaxUploadSize.HasValue ? (double?)(MaxUploadSize.Value / 1024.0) : null; }
            set { MaxUploadSize = value.HasValue ? (value * 1024) : null; }
        }

        public virtual double? MaxUploadSizeInMb
        {
            get { return MaxUploadSizeInKb.HasValue ? (double?)(MaxUploadSizeInKb.Value / 1024.0) : null; }
            set { MaxUploadSizeInKb = value.HasValue ? (value * 1024) : null; }
        }

        public virtual async Task<string> GenerateThumbHashAsync(IFile originalImage, IPathParser pathParser, IPictureEditor pictureEditor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThumbnailDirectory == null)
            {
                string md5 = await originalImage.GetFileMd5Async(cancellationToken);
                string thumbName = $"{Path.GetFileNameWithoutExtension(originalImage.Name)}_{md5}{originalImage.Extension}";
                string relativePath = GetRelativePath(originalImage.DirectoryName);
                return VolumeId + pathParser.Encode($"{relativePath}{DirectorySeparatorChar}{thumbName}");
            }
            else
            {
                string thumbPath = await GenerateThumbPathAsync(originalImage, pictureEditor, cancellationToken);
                string relativePath = thumbPath.Substring(ThumbnailDirectory.Length);
                return VolumeId + pathParser.Encode(relativePath);
            }
        }

        public virtual async Task<string> GenerateThumbPathAsync(IFile originalImage, IPictureEditor pictureEditor,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThumbnailDirectory == null
                || ThumbnailUrl == null
                || !pictureEditor.CanProcessFile(originalImage.Extension))
            {
                return null;
            }
            string relativePath = GetRelativePath(originalImage);
            string thumbDir = GetDirectoryName($"{ThumbnailDirectory}{relativePath}");
            string md5 = await originalImage.GetFileMd5Async(cancellationToken);
            string thumbName = $"{Path.GetFileNameWithoutExtension(originalImage.Name)}_{md5}{originalImage.Extension}";
            return $"{thumbDir}{DirectorySeparatorChar}{thumbName}";
        }

        public virtual Task<string> GenerateThumbPathAsync(IDirectory directory, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThumbnailDirectory == null)
            {
                return null;
            }

            string relativePath = GetRelativePath(directory);
            string thumbDir = ThumbnailDirectory + relativePath;
            return Task.FromResult(thumbDir);
        }

        public virtual bool IsRoot(IFileSystem fileSystem)
        {
            return fileSystem.FullName.Length == RootDirectory.Length;
        }

        public virtual bool IsRoot(string fullPath)
        {
            return fullPath.Length == RootDirectory.Length;
        }

        public virtual string GetRelativePath(IFileSystem fileSystem)
        {
            return fileSystem.FullName.Substring(RootDirectory.Length);
        }

        public virtual string GetRelativePath(string fullPath)
        {
            return fullPath.Substring(RootDirectory.Length);
        }

        public virtual string GetPathUrl(string fullPath)
        {
            return fullPath.Length == RootDirectory.Length
                ? Url
                : Url + fullPath.Substring(RootDirectory.Length + 1)
                    .Replace(DirectorySeparatorChar, WebConsts.UrlSegmentSeparator);
        }

        protected virtual string GetDirectoryName(string file)
        {
            var separatorIdx = file.LastIndexOf(DirectorySeparatorChar);
            return separatorIdx > -1 ? file.Substring(0, separatorIdx) : string.Empty;
        }

        public virtual bool Own(IFileSystem fileSystem)
        {
            return fileSystem.FullName == RootDirectory
                || fileSystem.FullName.StartsWith($"{RootDirectory}{DirectorySeparatorChar}");
        }

        public virtual bool Own(string fullPath)
        {
            return fullPath == RootDirectory
                || fullPath.StartsWith($"{RootDirectory}{DirectorySeparatorChar}");
        }
    }
}
