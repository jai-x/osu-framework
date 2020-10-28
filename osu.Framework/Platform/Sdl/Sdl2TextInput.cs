// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using SDL2;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Framework.Platform.Sdl
{
    public class Sdl2TextInput : ITextInputSource
    {
        private void dbg(string text) => Logger.GetLogger().Debug(text);

        // todo: IME activation logic
        public bool ImeActive => false;
        public event Action<string> OnNewImeComposition;
        public event Action<string> OnNewImeResult;

        private readonly Window window;

        private string pending = String.Empty;

        // hard casting as SDL is only used with the Window class anyway
        public Sdl2TextInput(IWindow window) => this.window = (Window) window;

        public string GetPendingText()
        {
            try
            {
                return pending;
            }
            finally
            {
                pending = String.Empty;
            }
        }

        private void handleTextInsert(string text)
        {
            dbg($"{nameof(Sdl2TextInput)} handleTextInsert: {text}");
            pending += text;
        }

        private void handleTextComposition(string text)
        {
            // todo: IME compostion logic
            dbg($"{nameof(Sdl2TextInput)} handleTextComposition: {text}");
        }

        public void Activate(object sender)
        {
            SDL.SDL_StartTextInput();
            window.TextInsert += handleTextInsert;
            window.TextComposition += handleTextComposition;
        }

        public void Deactivate(object sender)
        {
            SDL.SDL_StopTextInput();
            window.TextInsert -= handleTextInsert;
            window.TextComposition -= handleTextComposition;
        }
    }
}
