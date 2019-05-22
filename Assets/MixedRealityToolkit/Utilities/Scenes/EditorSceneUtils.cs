﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Reflection;
using Microsoft.MixedReality.Toolkit.SceneSystem;

namespace Microsoft.MixedReality.Toolkit.Utilities
{
    /// <summary>
    /// Utilities for loading / saving scenes in editor via SceneInfo.
    /// Because SceneInfo is defined in MixedRealityToolkit, this can't be kept in Editor utilities.
    /// </summary>
    public static class EditorSceneUtils
    {
        /// <summary>
        /// Enum used by this class to specify build settings order
        /// </summary>
        public enum BuildIndexTarget
        {
            First,
            None,
            Last,
        }

        public static SceneInfo CreateAndSaveScene(string sceneName, string path = null)
        {
            SceneInfo sceneInfo = default(SceneInfo);

            if (!EditorSceneManager.EnsureUntitledSceneHasBeenSaved("Save untitled scene before proceeding?"))
            {
                return sceneInfo;
            }

            Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            if (string.IsNullOrEmpty(path))
            {
                path = "Assets/" + sceneName + ".unity";
            }

            if (!EditorSceneManager.SaveScene(newScene, path))
            {
                Debug.LogError("Couldn't create and save scene " + sceneName + " at path " + path);
                return sceneInfo;
            }

            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            sceneInfo.Asset = sceneAsset;
            sceneInfo.Name = sceneAsset.name;
            sceneInfo.Path = path;

            return sceneInfo;
        }

        /// <summary>
        /// Adds scene to build settings.
        /// </summary>
        /// <param name="sceneObject">Scene object reference.</param>
        /// <param name="setAsFirst">Sets as first scene to be loaded.</param>
        public static bool AddSceneToBuildSettings(
            SceneInfo scene,
            EditorBuildSettingsScene[] scenes,
            BuildIndexTarget buildIndexTarget = BuildIndexTarget.None)
        {
            if (scene.IsEmpty)
            {   // Can't add a null scene to build settings
                return false;
            }

            long localID;
            string managerGuidString;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(scene.Asset, out managerGuidString, out localID);
            GUID sceneGuid = new GUID(managerGuidString);

            List<EditorBuildSettingsScene> newScenes = new List<EditorBuildSettingsScene>(scenes);
            // See if / where the scene exists in build settings
            int buildIndex = EditorSceneUtils.GetSceneBuildIndex(sceneGuid, newScenes);

            if (buildIndex < 0)
            {
                // It doesn't exist in the build settings, add it now
                switch (buildIndexTarget)
                {
                    case BuildIndexTarget.First:
                        // Add it to index 0
                        newScenes.Insert(0, new EditorBuildSettingsScene(sceneGuid, true));
                        break;

                    case BuildIndexTarget.None:
                    default:
                        // Just add it to the end
                        newScenes.Add(new EditorBuildSettingsScene(sceneGuid, true));
                        break;
                }

                EditorBuildSettings.scenes = newScenes.ToArray();
                return true;
            }
            else
            {
                switch (buildIndexTarget)
                {
                    // If it does exist, but isn't in the right spot, move it now
                    case BuildIndexTarget.First:
                        if (buildIndex != 0)
                        {
                            Debug.LogWarning("Scene '" + scene.Name + "' was not first in build order. Changing build settings now.");

                            newScenes.RemoveAt(buildIndex);
                            newScenes.Insert(0, new EditorBuildSettingsScene(sceneGuid, true));
                            EditorBuildSettings.scenes = newScenes.ToArray();
                        }
                        return true;

                    case BuildIndexTarget.Last:
                        if (buildIndex != EditorSceneManager.sceneCountInBuildSettings - 1)
                        {
                            newScenes.RemoveAt(buildIndex);
                            newScenes.Insert(newScenes.Count - 1, new EditorBuildSettingsScene(sceneGuid, true));
                            EditorBuildSettings.scenes = newScenes.ToArray();
                        }
                        return true;

                    case BuildIndexTarget.None:
                    default:
                        // Do nothing
                        return false;

                }
            }
        }

        /// <summary>
        /// Gets the build index for a scene GUID.
        /// There are many ways to do this in Unity but this is the only 100% reliable method I know of.
        /// </summary>
        /// <param name="sceneGUID"></param>
        /// <param name="scenes"></param>
        /// <returns></returns>
        public static int GetSceneBuildIndex(GUID sceneGUID, List<EditorBuildSettingsScene> scenes)
        {
            int buildIndex = -1;
            int sceneCount = 0;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].guid == sceneGUID)
                {
                    buildIndex = sceneCount;
                    break;
                }

                if (scenes[i].enabled)
                {
                    sceneCount++;
                }
            }

            return buildIndex;
        }

        /// <summary>
        /// Attempts to load scene in editor using a scene object reference.
        /// </summary>
        /// <param name="sceneObject">Scene object reference.</param>
        /// <param name="setAsFirst">Whether to set first in the heirarchy window.</param>
        /// <param name="editorScene">The loaded scene.</param>
        /// <returns>True if successful.</returns>
        public static bool LoadScene(SceneInfo sceneInfo, bool setAsFirst, out Scene editorScene)
        {
            editorScene = default(Scene);

            try
            {
                editorScene = EditorSceneManager.GetSceneByName(sceneInfo.Name);

                if (editorScene.isLoaded)
                {   // Already open - no need to do anything!
                    return true;
                }

                string scenePath = AssetDatabase.GetAssetOrScenePath(sceneInfo.Asset);
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                if (setAsFirst && EditorSceneManager.loadedSceneCount >= 1)
                {   // Move the scene to first in order in the heirarchy
                    Scene nextScene = EditorSceneManager.GetSceneAt(0);
                    EditorSceneManager.MoveSceneBefore(editorScene, nextScene);
                }
            }
            catch (InvalidOperationException)
            {
                // This can happen if we're trying to load immediately upon recompilation.
                return false;
            }
            catch (ArgumentException)
            {
                // This can happen if the scene is an invalid scene and we try to SetActive.
                return false;
            }
            catch (NullReferenceException)
            {
                // This can happen if the scene object is null.
                return false;
            }
            catch (MissingReferenceException)
            {
                // This can happen if the scene object is null.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns all root GameObjects in all open scenes.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<GameObject> GetRootGameObjectsInLoadedScenes()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene openScene = EditorSceneManager.GetSceneAt(i);
                if (!openScene.isLoaded)
                {   // Oh, Unity.
                    continue;
                }

                foreach (GameObject rootGameObject in openScene.GetRootGameObjects())
                    yield return rootGameObject;
            }
            yield break;
        }

        /// <summary>
        /// Unloads a scene in the editor and catches any errors that can happen along the way.
        /// </summary>
        /// <param name="sceneInfo"></param>
        /// <returns></returns>
        public static bool UnloadScene(SceneInfo sceneInfo, bool removeFromHeirarchy)
        {
            Scene editorScene = default(Scene);

            try
            {
                editorScene = EditorSceneManager.GetSceneByName(sceneInfo.Name);
                if (editorScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(editorScene, removeFromHeirarchy);
                }
            }
            catch (InvalidOperationException)
            {
                // This can happen if we're trying to load immediately upon recompilation.
                return false;
            }
            catch (ArgumentException)
            {
                // This can happen if the scene is an invalid scene and we try to SetActive.
                return false;
            }
            catch (NullReferenceException)
            {
                // This can happen if the scene object is null.
                return false;
            }
            catch (MissingReferenceException)
            {
                // This can happen if the scene object is null.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to set the active scene and catches all the various ways it can go wrong.
        /// Returns true if successful.
        /// </summary>
        /// <param name="scene"></param>
        /// <returns></returns>
        public static bool SetActiveScene(Scene scene)
        {
            try
            {
                EditorSceneManager.SetActiveScene(scene);
            }
            catch (InvalidOperationException)
            {
                // This can happen if we're trying to load immediately upon recompilation.
                return false;
            }
            catch (ArgumentException)
            {
                // This can happen if the scene is an invalid scene and we try to SetActive.
                return false;
            }
            catch (NullReferenceException)
            {
                // This can happen if the scene object is null.
                return false;
            }
            catch (MissingReferenceException)
            {
                // This can happen if the scene object is null.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Copies the lighting settings from the lighting scene to the active scene
        /// </summary>
        /// <param name="lightingScene"></param>
        public static void CopyLightingSettingsToActiveScene(Scene lightingScene)
        {
            // Store the active scene on entry
            Scene activeSceneOnEnter = EditorSceneManager.GetActiveScene();

            // No need to do anything
            if (activeSceneOnEnter == lightingScene)
                return;

            SerializedObject sourceLightmapSettings;
            SerializedObject sourceRenderSettings;

            // Set the active scene to the lighting scene
            SetActiveScene(lightingScene);
            // If we can't get the source settings for some reason, abort
            if (!GetLightmapAndRenderSettings(out sourceLightmapSettings, out sourceRenderSettings))
            {
                return;
            }

            bool madeChanges = false;

            // Set active scene back to the active scene on enter
            if (SetActiveScene(activeSceneOnEnter))
            {
                SerializedObject targetLightmapSettings;
                SerializedObject targetRenderSettings;

                if (GetLightmapAndRenderSettings(out targetLightmapSettings, out targetRenderSettings))
                {
                    madeChanges |= SerializedObjectUtils.CopySerializedObject(sourceLightmapSettings, targetLightmapSettings);
                    madeChanges |= SerializedObjectUtils.CopySerializedObject(sourceRenderSettings, targetRenderSettings);
                }
            }

            if (madeChanges)
            {
                Debug.LogWarning("Changed lighting settings in scene " + activeSceneOnEnter.name + " to match lighting scene " + lightingScene.name);
                EditorSceneManager.MarkSceneDirty(activeSceneOnEnter);
            }
        }

        /// <summary>
        /// Gets serialized objects for lightmap and render settings from active scene.
        /// </summary>
        /// <param name="lightmapSettings"></param>
        /// <param name="renderSettings"></param>
        /// <returns></returns>
        public static bool GetLightmapAndRenderSettings(out SerializedObject lightmapSettings, out SerializedObject renderSettings)
        {
            lightmapSettings = null;
            renderSettings = null;

            UnityEngine.Object lightmapSettingsObject = null;
            UnityEngine.Object renderSettingsObject = null;

            try
            {
                // Use reflection to get the serialized objects of lightmap settings and render settings
                Type lightmapSettingsType = typeof(LightmapEditorSettings);
                Type renderSettingsType = typeof(RenderSettings);

                lightmapSettingsObject = (UnityEngine.Object)lightmapSettingsType.GetMethod("GetLightmapSettings", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                renderSettingsObject = (UnityEngine.Object)renderSettingsType.GetMethod("GetRenderSettings", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
            catch (Exception)
            {
                Debug.LogWarning("Couldn't get lightmap or render settings. This version of Unity may not support this operation.");
                return false;
            }

            if (lightmapSettingsObject == null)
            {
                Debug.LogWarning("Couldn't get lightmap settings object");
                return false;
            }

            if (renderSettingsObject == null)
            {
                Debug.LogWarning("Couldn't get render settings object");
                return false;
            }

            // Store the settings in serialized objects
            lightmapSettings = new SerializedObject(lightmapSettingsObject);
            renderSettings = new SerializedObject(renderSettingsObject);
            return true;
        }

        /// <summary>
        /// Checks build settings for possible errors and displays warnings.
        /// </summary>
        /// <param name="allScenes"></param>
        /// <param name="duplicates"></param>
        /// <returns></returns>
        public static bool CheckBuildSettingsForDuplicates(List<SceneInfo> allScenes, Dictionary<string, List<int>> duplicates)
        {
            duplicates.Clear();
            List<int> indexes = null;
            bool foundDuplicate = false;

            foreach (SceneInfo sceneInfo in allScenes)
            {
                if (duplicates.TryGetValue(sceneInfo.Name, out indexes))
                {
                    indexes.Add(sceneInfo.BuildIndex);
                    foundDuplicate = true;
                }
                else
                {
                    duplicates.Add(sceneInfo.Name, new List<int> { sceneInfo.BuildIndex });
                }
            }

            return foundDuplicate;
        }
    }
}
#endif
