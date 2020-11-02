// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using SDL2;
using osu.Framework.Input;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Framework.Platform.Sdl
{
    public class Sdl2TextInput : ITextInputSource
    {
        #region Current ITextInputSource interface

        public string GetPendingText() => String.Empty;
        public bool ImeActive => false;
        public event Action<string> OnNewImeComposition;
        public event Action<string> OnNewImeResult;

        #endregion

        #region Proposed ITextInputSource interface

        public event Action<string> TextInsert;
        public event Action<string> TextComposition;

        public void StopTextComposition()
        {
            dbg($"{nameof(Sdl2TextInput)} StopTextComposition");

            var rect = textInputRect();
            dbg($"{nameof(Sdl2TextInput)} SetTextInputRect [x: {rect.x}, y: {rect.y}, w:{rect.w}, h:{rect.h}]");

            sdlWindow.ScheduleCommand(() =>
            {
                SDL.SDL_StopTextInput();
                SDL.SDL_StartTextInput();
                SDL.SDL_SetTextInputRect(ref rect);
            });
        }

        public void Activate(object sender)
        {
            dbg($"{nameof(Sdl2TextInput)} Activate");

            sdlWindow.TextInsert += handleTextInsert;
            sdlWindow.TextComposition += handleTextComposition;
            this.sender = sender as Drawable;

            var rect = textInputRect();
            dbg($"{nameof(Sdl2TextInput)} SetTextInputRect [x: {rect.x}, y: {rect.y}, w:{rect.w}, h:{rect.h}]");

            sdlWindow.ScheduleCommand(() =>
            {
                SDL.SDL_StartTextInput();
                SDL.SDL_SetTextInputRect(ref rect);
            });
        }

        public void Deactivate(object sender)
        {
            dbg($"{nameof(Sdl2TextInput)} Deactivate");

            sdlWindow.TextInsert -= handleTextInsert;
            sdlWindow.TextComposition -= handleTextComposition;
            this.sender = null;

            sdlWindow.ScheduleCommand(() => SDL.SDL_StopTextInput());
        }

        #endregion

        #region Sdl2TextInput implementation

        private void dbg(string msg) => Logger.GetLogger().Debug(msg);

        private readonly Sdl2WindowBackend sdlWindow;

        // hard casting as SDL is only used with the Window class anyway
        public Sdl2TextInput(IWindow window) => sdlWindow = (Sdl2WindowBackend) ((Window) window).WindowBackend;

        private void handleTextInsert(string text)
        {
            dbg($"{nameof(Sdl2TextInput)} handleTextInsert [{text}]");

            TextInsert?.Invoke(text);
        }

        private void handleTextComposition(string text)
        {
            dbg($"{nameof(Sdl2TextInput)} handleTextComposition [{text}]");

            TextComposition?.Invoke(text);
        }

        private Drawable sender;

        private SDL.SDL_Rect textInputRect()
        {
            if (sender == null)
                return new SDL.SDL_Rect();

            var s_rect = sender.ScreenSpaceDrawQuad;
            return new SDL.SDL_Rect
            {
                x = (int)s_rect.TopLeft.X,
                y = (int)s_rect.TopLeft.Y,
                w = (int)s_rect.Width,
                h = (int)s_rect.Height
            };
        }

        #endregion
    }
}
