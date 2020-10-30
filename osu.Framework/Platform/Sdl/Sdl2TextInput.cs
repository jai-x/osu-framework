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
            SDL.SDL_StopTextInput();
            SDL.SDL_StartTextInput();
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

        #endregion

        #region Sdl2TextInput implementation

        private void dbg(string msg) => Logger.GetLogger().Debug(msg);

        private readonly Window window;

        // hard casting as SDL is only used with the Window class anyway
        public Sdl2TextInput(IWindow window) => this.window = (Window) window;


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

        #endregion
    }
}
