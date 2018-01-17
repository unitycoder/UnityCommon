﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// <see cref="MonoBehaviour"/> based <see cref="IResourceProvider"/> implementation;
/// using <see cref="AsyncRunner"/>-derived classes for resource loading operations.
/// </summary>
[SpawnOnContextResolve(HideFlags.DontSave, true)]
public abstract class MonoRunnerResourceProvider : MonoBehaviour, IResourceProvider
{
    public event Action<float> OnLoadProgress;

    public bool IsLoading { get { return LoadProgress < 1f; } }
    public float LoadProgress { get; private set; }

    protected Dictionary<string, Resource> Resources = new Dictionary<string, Resource>();
    protected Dictionary<string, AsyncAction> Runners = new Dictionary<string, AsyncAction>();

    protected virtual void Awake ()
    {
        LoadProgress = 1f;
    }

    public virtual AsyncAction<Resource<T>> LoadResource<T> (string path) where T : class
    {
        if (Runners.ContainsKey(path))
            return Runners[path] as AsyncAction<Resource<T>>;

        if (Resources.ContainsKey(path))
            return AsyncAction<Resource<T>>.CreateCompleted(Resources[path] as Resource<T>);

        var resource = new Resource<T>(path);
        Resources.Add(path, resource);

        var loadRunner = CreateLoadRunner(resource);
        loadRunner.OnCompleted += HandleResourceLoaded;
        Runners.Add(path, loadRunner);
        UpdateLoadProgress();

        RunLoader(loadRunner);

        return loadRunner;
    }

    public virtual AsyncAction<List<Resource<T>>> LoadResources<T> (string path) where T : class
    {
        return LocateResourcesAtPath<T>(path).ThenAsync(HandleResourcesLocated);
    }

    public virtual void UnloadResource (string path)
    {
        if (!ResourceExists(path)) return;

        if (Runners.ContainsKey(path))
            CancelResourceLoading(path);

        var resource = Resources[path];
        Resources.Remove(path);
        UnloadResource(resource);
    }

    public virtual void UnloadResources ()
    {
        foreach (var resource in Resources.Values.ToList())
            UnloadResource(resource.Path);
    }

    public virtual bool ResourceExists (string path)
    {
        return Resources.ContainsKey(path);
    }

    protected abstract AsyncRunner<Resource<T>> CreateLoadRunner<T> (Resource<T> resource) where T : class;
    protected abstract AsyncAction<List<Resource<T>>> LocateResourcesAtPath<T> (string path) where T : class;
    protected abstract void UnloadResource (Resource resource);

    protected virtual void RunLoader<T> (AsyncRunner<Resource<T>> loader) where T : class
    {
        loader.Run();
    }

    protected virtual void CancelResourceLoading (string path)
    {
        if (!Runners.ContainsKey(path)) return;

        //Runners[path].Stop(); Unity .NET4.6 won't allow AsyncRunner<Resource<T>> cast to AsyncRunner<Resource>; waiting for fix.
        Runners[path].Reset();
        Runners.Remove(path);

        UpdateLoadProgress();
    }

    protected virtual void HandleResourceLoaded<T> (Resource<T> resource) where T : class
    {
        if (!resource.IsValid) Debug.LogError(string.Format("Resource '{0}' failed to load.", resource.Path));

        if (Runners.ContainsKey(resource.Path)) Runners.Remove(resource.Path);
        else Debug.LogWarning(string.Format("Load runner for resource '{0}' not found.", resource.Path));

        UpdateLoadProgress();
    }

    protected virtual AsyncAction<List<Resource<T>>> HandleResourcesLocated<T> (List<Resource<T>> locatedResources) where T : class
    {
        // Handle corner case when resources got loaded while locating.
        foreach (var locatedResource in locatedResources)
            if (!Resources.ContainsKey(locatedResource.Path) && locatedResource.IsValid)
                Resources.Add(locatedResource.Path, locatedResource);

        var loadRunners = locatedResources.Select(r => LoadResource<T>(r.Path)).ToArray();
        var loadAction = new AsyncAction<List<Resource<T>>>(loadRunners.Select(r => r.Result).ToList());
        new AsyncActionSet(loadRunners).Then(loadAction.CompleteInstantly);

        return loadAction;
    }

    protected virtual void UpdateLoadProgress ()
    {
        var prevProgress = LoadProgress;
        if (Runners.Count == 0) LoadProgress = 1f;
        else LoadProgress = Mathf.Min(1f / Runners.Count, .999f);
        if (prevProgress != LoadProgress) OnLoadProgress.SafeInvoke(LoadProgress);
    }
}
