﻿using System;
using UnityEngine;

namespace UnityCommon
{
    /// <summary>
    /// Represents available tween modes for <see cref="Color"/> values.
    /// </summary>
    public enum ColorTweenMode { All, RGB, Alpha }

    public struct ColorTween : ITweenValue
    {
        public event Action<Color> OnColorTween;

        public Color StartColor { get; set; }
        public Color TargetColor { get; set; }
        public ColorTweenMode TweenMode { get; set; }
        public EasingType EasingType { get; }
        public float TweenDuration { get; set; }
        public bool TimeScaleIgnored { get; set; }
        public bool TargetValid => OnColorTween != null;

        private readonly EasingFunction easingFunction;

        public ColorTween (Color from, Color to, ColorTweenMode mode, float time, Action<Color> onTween, bool ignoreTimeScale = false, EasingType easingType = default)
        {
            StartColor = from;
            TargetColor = to;
            TweenMode = mode;
            TweenDuration = time;
            EasingType = easingType;
            TimeScaleIgnored = ignoreTimeScale;
            OnColorTween = onTween;

            easingFunction = EasingType.GetEasingFunction();
        }

        public void TweenValue (float tweenPercent)
        {
            if (!TargetValid) return;

            var newColor = default(Color);
            newColor.r = TweenMode == ColorTweenMode.Alpha ? StartColor.r : easingFunction(StartColor.r, TargetColor.r, tweenPercent);
            newColor.g = TweenMode == ColorTweenMode.Alpha ? StartColor.g : easingFunction(StartColor.g, TargetColor.g, tweenPercent);
            newColor.b = TweenMode == ColorTweenMode.Alpha ? StartColor.b : easingFunction(StartColor.b, TargetColor.b, tweenPercent);
            newColor.a = TweenMode == ColorTweenMode.RGB ? StartColor.a : easingFunction(StartColor.a, TargetColor.a, tweenPercent);

            OnColorTween.Invoke(newColor);
        }

    }
}
