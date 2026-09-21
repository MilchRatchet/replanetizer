// Copyright (C) 2018-2026, The Replanetizer Contributors.
// Replanetizer is free software: you can redistribute it
// and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation,
// either version 3 of the License, or (at your option) any later version.
// Please see the LICENSE.md file for more details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ImGuiNET;
using LibReplanetizer.LevelObjects;
using OpenTK.Mathematics;

namespace Replanetizer.Frames
{
    public class HoverMetadataFrame : LevelSubFrame
    {
        protected sealed override string frameName { get; set; } = "Hovered object";

        private const float CURSOR_OFFSET = 12.0f;

        private static readonly ImGuiWindowFlags WINDOW_FLAGS =
            ImGuiWindowFlags.NoDecoration
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoInputs;

        public HoverMetadataFrame(Window wnd, LevelFrame levelFrame) : base(wnd, levelFrame)
        {
        }

        public override void RenderAsWindow(float deltaTime)
        {
            if (!levelFrame.TryGetHoveredObject(out LevelObject? hoveredObject, out System.Numerics.Vector2 screenPosition))
                return;
            if (hoveredObject == null)
                return;

            var viewport = ImGui.GetMainViewport();
            var viewportCenter = viewport.WorkPos + viewport.WorkSize * 0.5f;
            bool placeLeft = screenPosition.X > viewportCenter.X;
            bool placeAbove = screenPosition.Y > viewportCenter.Y;
            var windowPosition = screenPosition + new System.Numerics.Vector2(
                placeLeft ? -CURSOR_OFFSET : CURSOR_OFFSET,
                placeAbove ? -CURSOR_OFFSET : CURSOR_OFFSET);
            var pivot = new System.Numerics.Vector2(placeLeft ? 1.0f : 0.0f, placeAbove ? 1.0f : 0.0f);

            ImGui.SetNextWindowPos(windowPosition, ImGuiCond.Always, pivot);
            ImGui.SetNextWindowBgAlpha(0.85f);

            if (ImGui.Begin(frameName, WINDOW_FLAGS))
                RenderMetadata(hoveredObject);
            ImGui.End();
        }

        public override void Render(float deltaTime)
        {
        }

        private static void RenderMetadata(LevelObject hoveredObject)
        {
            ImGui.TextUnformatted(hoveredObject.GetType().Name);
            ImGui.TextUnformatted("Global ID: " + hoveredObject.globalID);

            foreach (PropertyInfo property in GetMetadataProperties(hoveredObject.GetType()))
            {
                object? value = property.GetValue(hoveredObject);
                if (value == null)
                    continue;

                ImGui.TextUnformatted(
                    (property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name) +
                    ": " + FormatValue(value));
            }
        }

        private static IEnumerable<PropertyInfo> GetMetadataProperties(Type objectType)
        {
            return objectType.GetProperties()
                .Where(property => property.GetIndexParameters().Length == 0)
                .Where(property => property.GetCustomAttribute<CategoryAttribute>()?.Category == "Attributes")
                .Where(property => IsSupportedType(property.PropertyType))
                .OrderBy(property => property.MetadataToken)
                .ThenBy(property => property.Name);
        }

        private static bool IsSupportedType(Type type)
        {
            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
            return underlyingType.IsPrimitive || underlyingType.IsEnum ||
                underlyingType == typeof(decimal) || underlyingType == typeof(string) ||
                underlyingType == typeof(Vector2) || underlyingType == typeof(Vector3) ||
                underlyingType == typeof(Vector4) || underlyingType == typeof(Quaternion);
        }

        private static string FormatValue(object value)
        {
            if (value is Vector2 vector2)
                return $"({vector2.X.ToString("G6", CultureInfo.InvariantCulture)}, {vector2.Y.ToString("G6", CultureInfo.InvariantCulture)})";
            if (value is Vector3 vector3)
                return $"({vector3.X.ToString("G6", CultureInfo.InvariantCulture)}, {vector3.Y.ToString("G6", CultureInfo.InvariantCulture)}, {vector3.Z.ToString("G6", CultureInfo.InvariantCulture)})";
            if (value is Vector4 vector4)
                return $"({vector4.X.ToString("G6", CultureInfo.InvariantCulture)}, {vector4.Y.ToString("G6", CultureInfo.InvariantCulture)}, {vector4.Z.ToString("G6", CultureInfo.InvariantCulture)}, {vector4.W.ToString("G6", CultureInfo.InvariantCulture)})";
            if (value is Quaternion quaternion)
                return $"({quaternion.X.ToString("G6", CultureInfo.InvariantCulture)}, {quaternion.Y.ToString("G6", CultureInfo.InvariantCulture)}, {quaternion.Z.ToString("G6", CultureInfo.InvariantCulture)}, {quaternion.W.ToString("G6", CultureInfo.InvariantCulture)})";
            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

            return value.ToString() ?? string.Empty;
        }
    }
}
