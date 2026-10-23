// Copyright (c) 2014-present, Facebook, Inc. All rights reserved.
//
// You are hereby granted a non-exclusive, worldwide, royalty-free license to use,
// copy, modify, and distribute this software in source code or binary form for use
// in connection with the web services and APIs provided by Facebook.
//
// As with any software that integrates with the Facebook platform, your use of
// this software is subject to the Facebook Developer Principles and Policies
// [http://developers.facebook.com/policy/]. This copyright notice shall be
// included in all copies or substantial portions of the software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
// FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
// IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
// CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#import <UIKit/UIKit.h>

// Value-tested, never with defined(): Unity sets the macro to 0 under the Objective-C Xcode
// project type and 1 under Swift, so defined() would take the Swift branch under both. How Unity
// delivers it to third-party plugin sources is unverified here — the macro is Unity 6.5+ only and
// no trampoline vendored in third-party/toolchains/unity is new enough to define it. Absence is
// the safe case either way: #if reads an undefined macro as 0, i.e. the Objective-C branch.
#if !UNITY_XCODE_PROJECT_TYPE_SWIFT
// Trampoline-only. The Swift Xcode project type replaces UnityAppController with UnityPlayer and
// publishes lifecycle through NotificationCenter, so neither this header nor the
// AppDelegateListener protocol exists there.
#import "AppDelegateListener.h"

//if we are on a version of unity that has the version number defined use it, otherwise we have added it ourselves in the post build step
#if HAS_UNITY_VERSION_DEF
#include "UnityTrampolineConfigure.h"
#endif
#endif

#if UNITY_XCODE_PROJECT_TYPE_SWIFT
@interface FBUnityInterface : NSObject
#else
@interface FBUnityInterface : NSObject <AppDelegateListener>
#endif
{
  //If you make changes in here make the same changes in Assets/Facebook/Scripts/NativeDialogModes.cs
  enum ShareDialogMode
  {
    AUTOMATIC = 0,
    NATIVE = 1,
    WEB = 2,
    FEED = 3,
  };
}

@property (assign, nonatomic) BOOL useFrictionlessRequests;
@property (nonatomic) ShareDialogMode shareDialogMode;

+ (FBUnityInterface *)sharedInstance;
@end
