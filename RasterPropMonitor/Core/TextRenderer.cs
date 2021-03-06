﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace JSI
{
    internal class TextRenderer
    {
        private class FontRenderer
        {
            public Texture2D fontTexture;
            public Mesh mesh;
            public GameObject obj;
            public Material fontMaterial;

            public List<Vector3> vertices = new List<Vector3>();
            public List<Vector2> uvs = new List<Vector2>();
            public List<Color32> colors32 = new List<Color32>();

            internal FontRenderer(Texture2D fontTexture, Vector2 vectorSize, int drawingLayer, Transform parentTransform)
            {
                Shader displayShader = JUtil.LoadInternalShader("RPM/FontShader");

                fontMaterial = new Material(displayShader);
                fontMaterial.color = Color.white;
                fontMaterial.mainTexture = fontTexture;

                this.fontTexture = fontTexture;
                this.fontTexture.filterMode = FilterMode.Bilinear;

                obj = new GameObject(fontTexture.name + "-FontRenderer");
                MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
                obj.AddComponent<MeshRenderer>();

                mesh = new Mesh();
                meshFilter.mesh = mesh;

                obj.layer = drawingLayer;

                UnityEngine.Object.Destroy(obj.GetComponent<Collider>());

                obj.transform.position = new Vector3(0, 0, 0.5f);
                obj.GetComponent<Renderer>().material = fontMaterial;
                obj.transform.parent = parentTransform;
            }

            internal void Bake()
            {
                mesh.Clear();

                if (vertices.Count == 0)
                {
                    return;
                }

                mesh.vertices = vertices.ToArray();
                mesh.uv = uvs.ToArray();

                if (colors32.Count > 0)
                {
                    Color32[] colorarr = new Color32[colors32.Count * 4];
                    for (int i = 0; i < colors32.Count; ++i)
                    {
                        colorarr[i * 4 + 0] = colors32[i];
                        colorarr[i * 4 + 1] = colors32[i];
                        colorarr[i * 4 + 2] = colors32[i];
                        colorarr[i * 4 + 3] = colors32[i];
                    }
                    mesh.colors32 = colorarr;
                }

                int quadCount = vertices.Count / 4;
                // 6 indices for each quad (4 vertices)
                int[] indices = new int[quadCount * 6];
                for (int i = 0; i < quadCount; ++i)
                {
                    indices[i * 6 + 0] = i * 4 + 1;
                    indices[i * 6 + 1] = i * 4 + 0;
                    indices[i * 6 + 2] = i * 4 + 2;
                    indices[i * 6 + 3] = i * 4 + 3;
                    indices[i * 6 + 4] = i * 4 + 1;
                    indices[i * 6 + 5] = i * 4 + 2;
                }

                mesh.triangles = indices;
            }

            internal void Clear()
            {
                vertices.Clear();
                uvs.Clear();
                colors32.Clear();
            }

            // MOARdV TODO: Make this do something
            internal void Destroy()
            {
                UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(obj);
                UnityEngine.Object.Destroy(fontMaterial);
            }
        }

        // The per-font (texture) renderers
        private readonly List<FontRenderer> fontRenderer;

        // pre-computed font sizes (in terms of pixel sizes)
        private readonly float fontLetterWidth;
        private readonly float fontLetterHeight;
        private readonly float fontLetterHalfHeight;
        private readonly float fontLetterHalfWidth;
        private readonly float fontLetterDoubleWidth;

        // Size of the screen in pixels
        private readonly int screenPixelWidth;
        private readonly int screenPixelHeight;

        // Offset to the top-left corner of the screen
        private readonly float screenXOffset, screenYOffset;

        // Supported characters
        private readonly Dictionary<char, Rect> fontCharacters = new Dictionary<char, Rect>();
        private readonly HashSet<char> characterWarnings = new HashSet<char>();

        // Stores the last strings we drew so we can determine if they've changed.
        private string cachedText;
        private string cachedOverlayText;

        private readonly bool manuallyInvertY;

        private GameObject cameraBody;
        private Camera textCamera;

        private enum Script
        {
            Normal,
            Subscript,
            Superscript,
        }

        private enum Width
        {
            Normal,
            Half,
            Double,
        }

        /**
         * TextRenderer (constructor)
         * 
         * Set up the TextRenderer object, and take care of the pre-computations needed.
         */
        public TextRenderer(List<Texture2D> fontTexture, Vector2 fontLetterSize, string fontDefinitionString, int drawingLayer, int screenWidth, int screenHeight)
        {
            if (fontTexture.Count == 0)
            {
                throw new Exception("No font textures found");
            }

            manuallyInvertY = false;
            //if (SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 9") || SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 11") || SystemInfo.graphicsDeviceVersion.StartsWith("Direct3D 12"))
            //{
            //    manuallyInvertY = (UnityEngine.QualitySettings.antiAliasing > 0);
            //}

            screenPixelWidth = screenWidth;
            screenPixelHeight = screenHeight;

            screenXOffset = (float)screenPixelWidth * -0.5f;
            screenYOffset = (float)screenPixelHeight * 0.5f - fontLetterSize.y;
            if (manuallyInvertY)
            {
                // This code was written for a much older flavor of Unity, and the Unity 2017.1 update broke
                // some assumptions about who managed the y-inversion issue between OpenGL and DX9.
                screenYOffset = -screenYOffset;
                screenYOffset -= fontLetterSize.y;
            }

            float fontLettersX = Mathf.Floor(fontTexture[0].width / fontLetterSize.x);
            float fontLettersY = Mathf.Floor(fontTexture[0].height / fontLetterSize.y);
            float pixelOffsetX = 0.5f / (float)fontTexture[0].width;
            float pixelOffsetY = 0.5f / (float)fontTexture[0].height;
            float letterSpanX = 1.0f / fontLettersX;
            float letterSpanY = 1.0f / fontLettersY;
            int lastCharacter = (int)fontLettersX * (int)fontLettersY;

            if (lastCharacter != fontDefinitionString.Length)
            {
                JUtil.LogMessage(this, "Warning, number of letters in the font definition does not match font bitmap size.");
            }

            // Precompute texture coordinates for all of the supported characters
            for (int i = 0; i < lastCharacter && i < fontDefinitionString.Length; i++)
            {
                int xSource = i % (int)fontLettersX;
                int ySource = (i - xSource) / (int)fontLettersX;
                if (!fontCharacters.ContainsKey(fontDefinitionString[i]))
                {
                    fontCharacters[fontDefinitionString[i]] = new Rect(letterSpanX * (float)xSource + pixelOffsetX, letterSpanY * (fontLettersY - (float)ySource - 1.0f) + pixelOffsetY, letterSpanX, letterSpanY);
                }
            }

            fontLetterWidth = fontLetterSize.x;
            fontLetterHeight = fontLetterSize.y;
            fontLetterHalfHeight = fontLetterSize.y * 0.5f;
            fontLetterHalfWidth = fontLetterSize.x * 0.5f;
            fontLetterDoubleWidth = fontLetterSize.x * 2.0f;

            // Set up the camera
            cameraBody = new GameObject();
            cameraBody.name = "RPMTextRender" + cameraBody.GetInstanceID();
            cameraBody.layer = drawingLayer;

            textCamera = cameraBody.AddComponent<Camera>();
            textCamera.enabled = false;
            textCamera.orthographic = true;
            textCamera.aspect = (float)screenWidth / (float)screenHeight;
            textCamera.eventMask = 0;
            textCamera.farClipPlane = 3f;
            textCamera.orthographicSize = (float)(screenHeight) * 0.5f;
            textCamera.cullingMask = 1 << drawingLayer;
            textCamera.clearFlags = CameraClearFlags.Nothing;
            textCamera.transparencySortMode = TransparencySortMode.Orthographic;
            textCamera.transform.position = Vector3.zero;
            textCamera.transform.LookAt(new Vector3(0.0f, 0.0f, 1.5f), Vector3.up);

            fontRenderer = new List<FontRenderer>();
            for (int i = 0; i < fontTexture.Count; ++i)
            {
                FontRenderer fr = new FontRenderer(fontTexture[i], fontLetterSize, drawingLayer, cameraBody.transform);

                fontRenderer.Add(fr);
            }
        }

        /**
         * ParseText
         *
         * Parse the text to render, accounting for tagged values (superscript, subscript, font, nudge, etc).
         */
        private void ParseText(string textToRender, int screenXMin, int screenYMin, Color defaultColor, int pageFont)
        {
            if (pageFont >= fontRenderer.Count)
            {
                pageFont = 0;
            }

            float yCursor = screenYMin * fontLetterHeight;
            Color32 fontColor = defaultColor;
            float xOffset = 0.0f;
            float yOffset = 0.0f;
            Script scriptType = Script.Normal;
            Width fontWidth = Width.Normal;
            FontRenderer fr = fontRenderer[pageFont];
            bool anyWarnings = false;

            float xCursor = screenXMin * fontLetterWidth;
            for (int charIndex = 0; charIndex < textToRender.Length; charIndex++)
            {
                bool escapedBracket = false;
                // We will continue parsing bracket pairs until we're out of bracket pairs,
                // since all of them -- except the escaped bracket tag --
                // consume characters and change state without actually generating any output.
                while (charIndex < textToRender.Length && textToRender[charIndex] == '[')
                {
                    // If there's no closing bracket, we stop parsing and go on to printing.
                    int nextBracket = textToRender.IndexOf(']', charIndex) - charIndex;
                    if (nextBracket < 1)
                        break;
                    // Much easier to parse it this way, although I suppose more expensive.
                    string tagText = textToRender.Substring(charIndex + 1, nextBracket - 1);
                    if ((tagText.Length == 9 || tagText.Length == 7) && tagText[0] == '#')
                    {
                        // Valid color tags are [#rrggbbaa] or [#rrggbb].
                        fontColor = XKCDColors.ColorTranslator.FromHtml(tagText);
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText.Length > 2 && tagText[0] == '@')
                    {
                        // Valid nudge tags are [@x<number>] or [@y<number>] so the conditions for them is that
                        // the next symbol is @ and there are at least three, one designating the axis.
                        float coord;
                        if (float.TryParse(tagText.Substring(2), out coord))
                        {
                            switch (tagText[1])
                            {
                                case 'X':
                                case 'x':
                                    xOffset = coord;
                                    break;
                                case 'Y':
                                case 'y':
                                    yOffset = coord;
                                    break;
                            }
                            // We only consume the symbols if they did parse correctly.
                            charIndex += nextBracket + 1;
                        }
                        else //If it didn't parse, skip over it.
                            break;
                    }
                    else if (tagText == "sup")
                    {
                        // Superscript!
                        scriptType = Script.Superscript;
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText == "sub")
                    {
                        // Subscript!
                        scriptType = Script.Subscript;
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText == "/sup" || tagText == "/sub")
                    {
                        // And back...
                        scriptType = Script.Normal;
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText == "hw")
                    {
                        fontWidth = Width.Half;
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText == "dw")
                    {
                        fontWidth = Width.Double;
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText == "/hw" || tagText == "/dw")
                    {
                        // And back...
                        fontWidth = Width.Normal;
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText.StartsWith("font", StringComparison.Ordinal))
                    {
                        int newFontID;
                        if (int.TryParse(tagText.Substring(4), out newFontID) && newFontID >= 0 && newFontID < fontRenderer.Count)
                        {
                            //fontTextureIndex = (int)newFontID;
                            fr = fontRenderer[newFontID];
                        }
                        charIndex += nextBracket + 1;
                    }
                    else if (tagText == "[")
                    {
                        // We got a "[[]" which means an escaped opening bracket.
                        escapedBracket = true;
                        charIndex += nextBracket;
                        break;
                    }
                    else // Else we didn't recognise anything so it's not a tag.
                    {
                        break;
                    }
                }

                if (string.Compare(textToRender, charIndex, Environment.NewLine, 0, Environment.NewLine.Length) == 0)
                {
                    // New line: Advance yCursor, reset xCursor and the various state values.
                    yCursor += fontLetterHeight;
                    xCursor = screenXMin * fontLetterWidth;
                    charIndex += Environment.NewLine.Length - 1;

                    fontColor = defaultColor;
                    xOffset = 0.0f;
                    yOffset = 0.0f;
                    fontWidth = Width.Normal;
                    scriptType = Script.Normal;
                    fr = fontRenderer[pageFont];
                }
                else
                {
                    float xPos = xCursor + xOffset;
                    float yPos = yCursor + yOffset;
                    if (charIndex < textToRender.Length &&
                        xPos < screenPixelWidth &&
                        xPos > -(fontWidth == Width.Normal ? fontLetterWidth : (fontWidth == Width.Half ? fontLetterHalfWidth : fontLetterDoubleWidth)) &&
                        yPos < screenPixelHeight &&
                        yPos > -fontLetterHeight)
                    {
                        if (!DrawChar(fr, escapedBracket ? '[' : textToRender[charIndex], xPos, yPos, fontColor, scriptType, fontWidth))
                        {
                            anyWarnings = true;
                        }
                    }
                    switch (fontWidth)
                    {
                        case Width.Normal:
                            xCursor += fontLetterWidth;
                            break;
                        case Width.Half:
                            xCursor += fontLetterHalfWidth;
                            break;
                        case Width.Double:
                            xCursor += fontLetterDoubleWidth;
                            break;

                    }
                }
            }

            if (anyWarnings)
            {
                JUtil.LogMessage(this, "String missing characters: {0}", textToRender);
            }
        }

        /**
         * Record the vertex, uv, and color information for a single character.
         */
        private bool DrawChar(FontRenderer fr, char letter, float xPos, float yPos, Color32 letterColor, Script scriptType, Width fontWidth)
        {
            if (fontCharacters.ContainsKey(letter))
            {
                // This code was written for a much older flavor of Unity, and the Unity 2017.1 update broke
                // some assumptions about who managed the y-inversion issue between OpenGL and DX9.
                float yPosition;
                if (manuallyInvertY)
                {
                    yPosition = screenYOffset + ((scriptType == Script.Superscript) ? yPos + fontLetterHalfHeight : yPos);
                }
                else
                {
                    yPosition = screenYOffset - ((scriptType == Script.Superscript) ? yPos - fontLetterHalfHeight : yPos);
                }
                Rect pos = new Rect(screenXOffset + xPos,
                    yPosition,
                        (fontWidth == Width.Normal ? fontLetterWidth : (fontWidth == Width.Half ? fontLetterHalfWidth : fontLetterDoubleWidth)),
                        (scriptType != Script.Normal) ? fontLetterHalfHeight : fontLetterHeight);
                fr.vertices.Add(new Vector3(pos.xMin, pos.yMin, 0.0f));
                fr.vertices.Add(new Vector3(pos.xMax, pos.yMin, 0.0f));
                fr.vertices.Add(new Vector3(pos.xMin, pos.yMax, 0.0f));
                fr.vertices.Add(new Vector3(pos.xMax, pos.yMax, 0.0f));

                Rect uv = fontCharacters[letter];
                fr.uvs.Add(new Vector2(uv.xMin, (manuallyInvertY) ? uv.yMax : uv.yMin));
                fr.uvs.Add(new Vector2(uv.xMax, (manuallyInvertY) ? uv.yMax : uv.yMin));
                fr.uvs.Add(new Vector2(uv.xMin, (manuallyInvertY) ? uv.yMin : uv.yMax));
                fr.uvs.Add(new Vector2(uv.xMax, (manuallyInvertY) ? uv.yMin : uv.yMax));

                fr.colors32.Add(letterColor);
            }
            else if (!characterWarnings.Contains(letter))
            {
                JUtil.LogMessage(this, "Warning: Attempted to print a character \"{0}\" (u{1}) not present in the font.", letter.ToString(), letter);

                characterWarnings.Add(letter);
                return false;
            }

            return true;
        }

        /**
         * Render the text.  Assumes screen has already been cleared, so all we have to do here
         * is prepare the text objects and draw the text.
         */
        public void Render(RenderTexture screen, MonitorPage activePage)
        {
            bool textDirty = (cachedText != activePage.Text) || (cachedOverlayText != activePage.textOverlay);

            if (textDirty)
            {
                cachedText = activePage.Text;
                cachedOverlayText = activePage.textOverlay;

                for (int i = 0; i < fontRenderer.Count; ++i)
                {
                    fontRenderer[i].Clear();
                }

                if (!string.IsNullOrEmpty(activePage.Text))
                {
                    ParseText(activePage.Text, activePage.screenXMin, activePage.screenYMin, activePage.defaultColor, activePage.pageFont);
                }

                if (!string.IsNullOrEmpty(activePage.textOverlay))
                {
                    ParseText(activePage.textOverlay, 0, 0, activePage.defaultColor, activePage.pageFont);
                }

                for (int i = 0; i < fontRenderer.Count; ++i)
                {
                    fontRenderer[i].Bake();
                }
            }

            for (int i = 0; i < fontRenderer.Count; ++i)
            {
                if (fontRenderer[i].mesh.vertexCount > 0)
                {
                    JUtil.ShowHide(true, fontRenderer[i].obj);
                }
            }

            textCamera.targetTexture = screen;
            textCamera.Render();

            for (int i = 0; i < fontRenderer.Count; ++i)
            {
                if (fontRenderer[i].mesh.vertexCount > 0)
                {
                    JUtil.ShowHide(false, fontRenderer[i].obj);
                }
            }
        }
    }
}
