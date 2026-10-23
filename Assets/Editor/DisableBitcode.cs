/**
 * Copyright (c) 2014-present, Facebook, Inc. All rights reserved.
 *
 * You are hereby granted a non-exclusive, worldwide, royalty-free license to use,
 * copy, modify, and distribute this software in source code or binary form for use
 * in connection with the web services and APIs provided by Facebook.
 *
 * As with any software that integrates with the Facebook platform, your use of
 * this software is subject to the Facebook Developer Principles and Policies
 * [http://developers.facebook.com/policy/]. This copyright notice shall be
 * included in all copies or substantial portions of the software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
 * FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
 * COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System.IO;
using UnityEngine;
using UnityEditor;
#if UNITY_IOS
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
#endif
using UnityEditor.Callbacks;


namespace Facebook.Unity.PostProcess
{
    /// <summary>
    /// Automatically disables Bitcode on iOS builds
    /// </summary>
    public static class DisableBitcode
    {
        [PostProcessBuildAttribute(999)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuildProject)
        {
#if UNITY_IOS
            if (buildTarget != BuildTarget.iOS) return;
            // Resolved, not assumed: only the Objective-C project type is named statically.
            string projectPath = PBXProject.GetPBXProjectPath(pathToBuildProject);
            PBXProject pbxProject = new PBXProject();
            pbxProject.ReadFromFile(projectPath);

            //Disabling Bitcode on all targets
            //Main
            DisableBitcodeOnTarget(pbxProject, pbxProject.GetUnityMainTargetGuid());
            //Unity Tests - not emitted by every Xcode project type
            DisableBitcodeOnTarget(pbxProject, pbxProject.TargetGuidByName(PBXProject.GetUnityTestTargetName()));
            //Unity Framework
            DisableBitcodeOnTarget(pbxProject, pbxProject.GetUnityFrameworkTargetGuid());

            pbxProject.WriteToFile(projectPath);
#endif
        }

#if UNITY_IOS
        private static void DisableBitcodeOnTarget(PBXProject pbxProject, string targetGuid)
        {
            if (!string.IsNullOrEmpty(targetGuid))
            {
                pbxProject.SetBuildProperty(targetGuid, "ENABLE_BITCODE", "NO");
            }
        }
#endif
    }
}
