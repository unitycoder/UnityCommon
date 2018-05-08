﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityGoogleDrive;

public class GoogleDriveResourceLoader<TResource> : LoadResourceRunner<TResource> where TResource : class
{
    public string RootPath { get; private set; }

    private readonly List<Type> NATIVE_REQUEST_TYPES = new List<Type> { typeof(AudioClip), typeof(Texture2D) };

    private bool useNativeRequests;
    private Action<string> logAction;
    private GoogleDriveRequest downloadRequest;
    private GoogleDriveFiles.ListRequest listRequest;
    private IRawConverter<TResource> converter;
    private RawDataRepresentation usedRepresentation;
    private byte[] rawData;

    public GoogleDriveResourceLoader (string rootPath, Resource<TResource> resource, 
        IRawConverter<TResource> converter, Action<string> logAction = null)
    {
        RootPath = rootPath;
        Resource = resource;
        useNativeRequests = NATIVE_REQUEST_TYPES.Contains(typeof(TResource));
        this.logAction = logAction;

        // MP3 is not supported in native requests on the standalone platforms. Fallback to raw converters.
        #if UNITY_STANDALONE || UNITY_EDITOR
        foreach (var r in converter.Representations)
            if (WebUtils.EvaluateAudioTypeFromMime(r.MimeType) == AudioType.MPEG) useNativeRequests = false;
        #endif

        this.converter = converter;
        usedRepresentation = new RawDataRepresentation();
    }

    public override async Task Run ()
    {
        await base.Run();

        var startTime = Time.time;
        var usedCache = false;

        // 1. Corner case when loading folders.
        if (typeof(TResource) == typeof(Folder))
        {
            (Resource as Resource<Folder>).Object = new Folder(Resource.Path);
            HandleOnCompleted();
            return;
        }

        // 2. Check if cached version of the file could be used.
        rawData = await TryLoadFileCacheAsync(Resource.Path);

        // 3. Cached version is not valid or doesn't exist; download or export the file.
        if (rawData == null)
        {
            // 4. Load file metadata from Google Drive.
            var filePath = string.IsNullOrEmpty(RootPath) ? Resource.Path : string.Concat(RootPath, '/', Resource.Path);
            var fileMeta = await GetFileMetaAsync(filePath);
            if (fileMeta == null) { Debug.LogError($"Failed to resolve '{filePath}' google drive metadata."); HandleOnCompleted(); return; }

            if (converter is IGoogleDriveConverter<TResource>) rawData = await ExportFileAsync(fileMeta);
            else rawData = await DownloadFileAsync(fileMeta);

            // 5. Cache the downloaded file.
            await WriteFileCacheAsync(Resource.Path, fileMeta.Id, rawData);
        }
        else usedCache = true; 

        // In case we used native requests the resource will already be set, so no need to use converters.
        if (!Resource.IsValid) Resource.Object = await converter.ConvertAsync(rawData);

        logAction?.Invoke($"Resource '{Resource.Path}' loaded {(rawData.Length / 1024f) / 1024f:0.###}MB over {Time.time - startTime:0.###} seconds from " + (usedCache ? "cache." : "Google Drive."));

        HandleOnCompleted();
    }

    public override void Cancel ()
    {
        base.Cancel();

        if (downloadRequest != null)
        {
            downloadRequest.Abort();
            downloadRequest = null;
        }

        if (listRequest != null)
        {
            listRequest.Abort();
            listRequest = null;
        }
    }

    protected override void HandleOnCompleted ()
    {
        if (downloadRequest != null) downloadRequest.Dispose();
        if (listRequest != null) listRequest.Dispose();

        base.HandleOnCompleted();
    }

    private async Task<UnityGoogleDrive.Data.File> GetFileMetaAsync (string filePath)
    {
        var fileName = filePath.GetAfter("/");
        var parentNames = filePath.GetBeforeLast("/").Split('/');

        // 1. Resolving folder ids one by one to find id of the file's parent folder.
        var parendId = "root"; // 'root' is alias id for the root folder in Google Drive.
        for (int i = 0; i < parentNames.Length; i++)
        {
            listRequest = new GoogleDriveFiles.ListRequest();
            listRequest.Fields = new List<string> { "files(id)" };
            listRequest.Q = $"'{parendId}' in parents and name = '{parentNames[i]}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";

            await listRequest.Send();

            if (!IsResultFound(listRequest))
            {
                Debug.LogError($"Failed to retrieve '{parentNames[i]}' part of '{filePath}' resource from Google Drive.");
                return null;
            }

            if (listRequest.ResponseData.Files.Count > 1)
                Debug.LogWarning($"Multiple '{parentNames[i]}' folders been found in Google Drive.");

            parendId = listRequest.ResponseData.Files[0].Id;
        }

        // 2. Searching the file and getting the metadata.
        var usedRepresentation = new RawDataRepresentation();
        foreach (var representation in converter.Representations)
        {
            usedRepresentation = representation;
            listRequest = new GoogleDriveFiles.ListRequest();
            listRequest.Fields = new List<string> { "files(id, modifiedTime, mimeType)" };
            var fullName = string.IsNullOrEmpty(representation.Extension) ? fileName : string.Concat(fileName, ".", representation.Extension);
            listRequest.Q = $"'{parendId}' in parents and name = '{fullName}' and mimeType = '{representation.MimeType}' and trashed = false";

            await listRequest.Send();

            if (IsResultFound(listRequest)) break;
        }

        if (!IsResultFound(listRequest))
        {
            Debug.LogError($"Failed to retrieve '{Resource.Path}.{usedRepresentation.Extension}' resource from Google Drive.");
            return null;
        }

        if (listRequest.ResponseData.Files.Count > 1)
            Debug.LogWarning($"Multiple '{Resource.Path}.{usedRepresentation.Extension}' files been found in Google Drive.");

        return listRequest.ResponseData.Files[0];
    }

    private async Task<byte[]> DownloadFileAsync (UnityGoogleDrive.Data.File fileMeta)
    {
        if (useNativeRequests)
        {
            if (typeof(TResource) == typeof(AudioClip)) downloadRequest = GoogleDriveFiles.DownloadAudio(fileMeta);
            else if (typeof(TResource) == typeof(Texture2D)) downloadRequest = GoogleDriveFiles.DownloadTexture(fileMeta.Id, true);
        }
        else downloadRequest = new GoogleDriveFiles.DownloadRequest(fileMeta);

        await downloadRequest.SendNonGeneric();
        if (downloadRequest.IsError || downloadRequest.GetResponseData<UnityGoogleDrive.Data.File>().Content == null)
        {
            Debug.LogError($"Failed to download {Resource.Path}.{usedRepresentation.Extension} resource from Google Drive.");
            return null;
        }

        if (useNativeRequests)
        {
            if (typeof(TResource) == typeof(AudioClip)) (Resource as Resource<AudioClip>).Object = downloadRequest.GetResponseData<UnityGoogleDrive.Data.AudioFile>().AudioClip;
            else if (typeof(TResource) == typeof(Texture2D)) (Resource as Resource<Texture2D>).Object = downloadRequest.GetResponseData<UnityGoogleDrive.Data.TextureFile>().Texture;
        }

        return downloadRequest.GetResponseData<UnityGoogleDrive.Data.File>().Content;
    }

    private async Task<byte[]> ExportFileAsync (UnityGoogleDrive.Data.File fileMeta)
    {
        Debug.Assert(converter is IGoogleDriveConverter<TResource>);

        var gDriveConverter = converter as IGoogleDriveConverter<TResource>;

        downloadRequest = new GoogleDriveFiles.ExportRequest(fileMeta.Id, gDriveConverter.ExportMimeType);
        await downloadRequest.SendNonGeneric();
        if (downloadRequest.IsError || downloadRequest.GetResponseData<UnityGoogleDrive.Data.File>().Content == null)
        {
            Debug.LogError($"Failed to export '{Resource.Path}' resource from Google Drive.");
            return null;
        }
        return downloadRequest.GetResponseData<UnityGoogleDrive.Data.File>().Content;
    }

    private async Task<byte[]> TryLoadFileCacheAsync (string resourcePath)
    {
        resourcePath = resourcePath.Replace("/", GoogleDriveResourceProvider.SLASH_REPLACE);
        var filePath = string.Concat(GoogleDriveResourceProvider.CACHE_DIR_PATH, "/", resourcePath);
        //if (!string.IsNullOrEmpty(usedRepresentation.Extension))
        //    filePath += string.Concat(".", usedRepresentation.Extension);
        if (!File.Exists(filePath)) return null;

        if (useNativeRequests)
        {
            // Web requests over IndexedDB are not supported; we should either use raw converters or disable caching.
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // Binary convertion of the audio is fucked on WebGL (can't use buffers), so disable caching here.
                if (typeof(TResource) == typeof(AudioClip)) return null;
                // Use raw converters for other native types.
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, true))
                {
                    var cachedData = new byte[fileStream.Length];
                    await fileStream.ReadAsync(cachedData, 0, (int)fileStream.Length);
                    return cachedData;
                }
            }

            UnityWebRequest request = null;
            if (typeof(TResource) == typeof(AudioClip)) request = UnityWebRequestMultimedia.GetAudioClip(filePath, WebUtils.EvaluateAudioTypeFromMime(converter.Representations[0].MimeType));
            else if (typeof(TResource) == typeof(Texture2D)) request = UnityWebRequestTexture.GetTexture(filePath, true);
            using (request)
            {
                await request.SendWebRequest();

                if (typeof(TResource) == typeof(AudioClip)) (Resource as Resource<AudioClip>).Object = DownloadHandlerAudioClip.GetContent(request);
                else if (typeof(TResource) == typeof(Texture2D)) (Resource as Resource<Texture2D>).Object = DownloadHandlerTexture.GetContent(request);
                return request.downloadHandler.data;
            }
        }
        else
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, true))
            {
                var cachedData = new byte[fileStream.Length];
                await fileStream.ReadAsync(cachedData, 0, (int)fileStream.Length);
                return cachedData;
            }
        }
    }

    private async Task WriteFileCacheAsync (string resourcePath, string fileId, byte[] fileRawData)
    {
        resourcePath = resourcePath.Replace("/", GoogleDriveResourceProvider.SLASH_REPLACE);
        var filePath = string.Concat(GoogleDriveResourceProvider.CACHE_DIR_PATH, "/", resourcePath);
        //if (!string.IsNullOrEmpty(usedRepresentation.Extension))
        //    filePath += string.Concat(".", usedRepresentation.Extension);

        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, fileRawData.Length, true))
            await fileStream.WriteAsync(fileRawData, 0, fileRawData.Length);

        // Flush cached file writes to IndexedDB on WebGL.
        // https://forum.unity.com/threads/webgl-filesystem.294358/#post-1940712
        #if UNITY_WEBGL && !UNITY_EDITOR
        WebGLExtensions.SyncFs();
        #endif

        // Add info for the smart caching policy.
        PlayerPrefs.SetString(string.Concat(GoogleDriveResourceProvider.SMART_CACHE_KEY_PREFIX, fileId), resourcePath);
    }

    private bool IsResultFound (GoogleDriveFiles.ListRequest request)
    {
        return listRequest != null && !listRequest.IsError && listRequest.ResponseData.Files != null && listRequest.ResponseData.Files.Count > 0;
    }
}
