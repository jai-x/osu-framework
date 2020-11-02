﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Caching;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Threading;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Platform;
using osu.Framework.Platform.Sdl;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Timing;

namespace osu.Framework.Graphics.UserInterface
{
    public abstract class TextBox : TabbableContainer, IHasCurrentValue<string>, IKeyBindingHandler<PlatformAction>
    {
        protected FillFlowContainer TextFlow { get; private set; }
        protected Container TextContainer { get; private set; }

        public override bool HandleNonPositionalInput => HasFocus;

        /// <summary>
        /// Padding to be used within the TextContainer. Requires special handling due to the sideways scrolling of text content.
        /// </summary>
        protected virtual float LeftRightPadding => 5;

        public int? LengthLimit;

        /// <summary>
        /// Whether clipboard copying functionality is allowed.
        /// </summary>
        protected virtual bool AllowClipboardExport => true;

        /// <summary>
        /// Whether seeking to word boundaries is allowed.
        /// </summary>
        protected virtual bool AllowWordNavigation => true;

        //represents the left/right selection coordinates of the word double clicked on when dragging
        private int[] doubleClickWord;

        /// <summary>
        /// Whether this TextBox should accept left and right arrow keys for navigation.
        /// </summary>
        public virtual bool HandleLeftRightArrows => true;

        /// <summary>
        /// Check if a character can be added to this TextBox.
        /// </summary>
        /// <param name="character">The pending character.</param>
        /// <returns>Whether the character is allowed to be added.</returns>
        protected virtual bool CanAddCharacter(char character) => true;

        public bool ReadOnly;

        /// <summary>
        /// Whether the textbox should rescind focus on commit.
        /// </summary>
        public bool ReleaseFocusOnCommit { get; set; } = true;

        /// <summary>
        /// Whether a commit should be triggered whenever the textbox loses focus.
        /// </summary>
        public bool CommitOnFocusLost { get; set; }

        public override bool CanBeTabbedTo => !ReadOnly;

        // Hardcoding text input type for proof of concept
        private Sdl2TextInput textInput;

        private Clipboard clipboard;

        private readonly Caret caret;

        public delegate void OnCommitHandler(TextBox sender, bool newText);

        /// <summary>
        /// Fired whenever text is committed via a user action.
        /// This usually happens on pressing enter, but can also be triggered on focus loss automatically, via <see cref="CommitOnFocusLost"/>.
        /// </summary>
        public event OnCommitHandler OnCommit;

        private readonly Scheduler textUpdateScheduler = new Scheduler(() => ThreadSafety.IsUpdateThread, null);

        protected TextBox()
        {
            Masking = true;

            Children = new Drawable[]
            {
                TextContainer = new Container
                {
                    AutoSizeAxes = Axes.X,
                    RelativeSizeAxes = Axes.Y,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Position = new Vector2(LeftRightPadding, 0),
                    Children = new Drawable[]
                    {
                        Placeholder = CreatePlaceholder(),
                        caret = CreateCaret(),
                        TextFlow = new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Direction = FillDirection.Horizontal,
                            AutoSizeAxes = Axes.X,
                            RelativeSizeAxes = Axes.Y,
                        },
                    },
                },
            };

            Current.ValueChanged += e => { Text = e.NewValue; };
            caret.Hide();
        }

        [BackgroundDependencyLoader]
        private void load(GameHost host)
        {
            // Hard casting for proof of concept, will crash when not using SDL
            textInput = (Sdl2TextInput)host.GetTextInput();
            clipboard = host.GetClipboard();

            if (textInput == null)
                return;

            textInput.TextInsert += handleTextInsert;
            textInput.TextComposition += handleTextComposition;
        }

        public virtual bool OnPressed(PlatformAction action)
        {
            int? amount = null;

            if (!HasFocus)
                return false;

            if (!HandleLeftRightArrows &&
                action.ActionMethod == PlatformActionMethod.Move &&
                (action.ActionType == PlatformActionType.CharNext || action.ActionType == PlatformActionType.CharPrevious))
                return false;

            if (compositionActive)
                resetComposition();

            switch (action.ActionType)
            {
                // Clipboard
                case PlatformActionType.Cut:
                case PlatformActionType.Copy:
                    if (string.IsNullOrEmpty(SelectedText) || !AllowClipboardExport) return true;

                    clipboard?.SetText(SelectedText);

                    if (action.ActionType == PlatformActionType.Cut)
                    {
                        string removedText = removeSelection();
                        OnUserTextRemoved(removedText);
                    }

                    return true;

                case PlatformActionType.Paste:
                    //the text may get pasted into the hidden textbox, so we don't need any direct clipboard interaction here.
                    string pending = textInput?.GetPendingText();

                    if (string.IsNullOrEmpty(pending))
                        pending = clipboard?.GetText();

                    InsertString(pending);
                    return true;

                case PlatformActionType.SelectAll:
                    selectionStart = 0;
                    selectionEnd = text.Length;
                    cursorAndLayout.Invalidate();
                    return true;

                // Cursor Manipulation
                case PlatformActionType.CharNext:
                    amount = 1;
                    break;

                case PlatformActionType.CharPrevious:
                    amount = -1;
                    break;

                case PlatformActionType.LineEnd:
                    amount = text.Length;
                    break;

                case PlatformActionType.LineStart:
                    amount = -text.Length;
                    break;

                case PlatformActionType.WordNext:
                    if (!AllowWordNavigation)
                        amount = 1;
                    else
                    {
                        int searchNext = Math.Clamp(selectionEnd, 0, Math.Max(0, Text.Length - 1));
                        while (searchNext < Text.Length && text[searchNext] == ' ')
                            searchNext++;
                        int nextSpace = text.IndexOf(' ', searchNext);
                        amount = (nextSpace >= 0 ? nextSpace : text.Length) - selectionEnd;
                    }

                    break;

                case PlatformActionType.WordPrevious:
                    if (!AllowWordNavigation)
                        amount = -1;
                    else
                    {
                        int searchPrev = Math.Clamp(selectionEnd - 2, 0, Math.Max(0, Text.Length - 1));
                        while (searchPrev > 0 && text[searchPrev] == ' ')
                            searchPrev--;
                        int lastSpace = text.LastIndexOf(' ', searchPrev);
                        amount = lastSpace > 0 ? -(selectionEnd - lastSpace - 1) : -selectionEnd;
                    }

                    break;
            }

            if (amount.HasValue)
            {
                switch (action.ActionMethod)
                {
                    case PlatformActionMethod.Move:
                        resetSelection();
                        moveSelection(amount.Value, false);
                        break;

                    case PlatformActionMethod.Select:
                        moveSelection(amount.Value, true);
                        break;

                    case PlatformActionMethod.Delete:
                        if (selectionLength == 0)
                            selectionEnd = Math.Clamp(selectionStart + amount.Value, 0, text.Length);

                        if (selectionLength > 0)
                        {
                            string removedText = removeSelection();
                            OnUserTextRemoved(removedText);
                        }

                        break;
                }

                return true;
            }

            return false;
        }

        public virtual void OnReleased(PlatformAction action)
        {
        }

        internal override void UpdateClock(IFrameBasedClock clock)
        {
            base.UpdateClock(clock);
            textUpdateScheduler.UpdateClock(Clock);
        }

        private void resetSelection()
        {
            selectionStart = selectionEnd;
            cursorAndLayout.Invalidate();
        }

        protected override void Dispose(bool isDisposing)
        {
            OnCommit = null;

            if (HasFocus)
                unbindInput();

            base.Dispose(isDisposing);
        }

        private float textContainerPosX;

        private string textAtLastLayout = string.Empty;

        private void updateCursorAndLayout()
        {
            Placeholder.Font = Placeholder.Font.With(size: CalculatedTextSize);

            textUpdateScheduler.Update();

            float cursorPos = 0;
            if (text.Length > 0)
                cursorPos = getPositionAt(selectionLeft);

            float cursorPosEnd = getPositionAt(selectionEnd);

            float? selectionWidth = null;
            if (selectionLength > 0)
                selectionWidth = getPositionAt(selectionRight) - cursorPos;

            float cursorRelativePositionAxesInBox = (cursorPosEnd - textContainerPosX) / DrawWidth;

            //we only want to reposition the view when the cursor reaches near the extremities.
            if (cursorRelativePositionAxesInBox < 0.1 || cursorRelativePositionAxesInBox > 0.9)
            {
                textContainerPosX = cursorPosEnd - DrawWidth / 2 + LeftRightPadding * 2;
            }

            textContainerPosX = Math.Clamp(textContainerPosX, 0, Math.Max(0, TextFlow.DrawWidth - DrawWidth + LeftRightPadding * 2));

            TextContainer.MoveToX(LeftRightPadding - textContainerPosX, 300, Easing.OutExpo);

            if (HasFocus)
                caret.DisplayAt(new Vector2(cursorPos, 0), selectionWidth);

            if (textAtLastLayout != text)
                Current.Value = text;

            if (textAtLastLayout.Length == 0 || text.Length == 0)
            {
                if (text.Length == 0)
                    Placeholder.Show();
                else
                    Placeholder.Hide();
            }

            textAtLastLayout = text;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            //have to run this after children flow
            if (!cursorAndLayout.IsValid)
            {
                updateCursorAndLayout();
                cursorAndLayout.Validate();
            }
        }

        private float getPositionAt(int index)
        {
            if (index > 0)
            {
                if (index < text.Length)
                    return TextFlow.Children[index].DrawPosition.X + TextFlow.DrawPosition.X;

                var d = TextFlow.Children[index - 1];
                return d.DrawPosition.X + d.DrawSize.X + TextFlow.Spacing.X + TextFlow.DrawPosition.X;
            }

            return 0;
        }

        private int getCharacterClosestTo(Vector2 pos)
        {
            pos = Parent.ToSpaceOfOtherDrawable(pos, TextFlow);

            int i = 0;

            foreach (Drawable d in TextFlow.Children)
            {
                if (d.DrawPosition.X + d.DrawSize.X / 2 > pos.X)
                    break;

                i++;
            }

            return i;
        }

        private int selectionStart;
        private int selectionEnd;

        private int selectionLength => Math.Abs(selectionEnd - selectionStart);

        private int selectionLeft => Math.Min(selectionStart, selectionEnd);
        private int selectionRight => Math.Max(selectionStart, selectionEnd);

        private readonly Cached cursorAndLayout = new Cached();

        private void moveSelection(int offset, bool expand)
        {
            if (compositionActive)
                return;

            int oldStart = selectionStart;
            int oldEnd = selectionEnd;

            if (expand)
                selectionEnd = Math.Clamp(selectionEnd + offset, 0, text.Length);
            else
            {
                if (selectionLength > 0 && Math.Abs(offset) <= 1)
                {
                    //we don't want to move the location when "removing" an existing selection, just set the new location.
                    if (offset > 0)
                        selectionEnd = selectionStart = selectionRight;
                    else
                        selectionEnd = selectionStart = selectionLeft;
                }
                else
                    selectionEnd = selectionStart = Math.Clamp((offset > 0 ? selectionRight : selectionLeft) + offset, 0, text.Length);
            }

            if (oldStart != selectionStart || oldEnd != selectionEnd)
            {
                OnCaretMoved(expand);
                cursorAndLayout.Invalidate();
            }
        }

        /// <summary>
        /// Removes the selected text if a selection persists.
        /// </summary>
        private string removeSelection() => removeCharacters(selectionLength);

        /// <summary>
        /// Removes a specified <paramref name="number"/> of characters left side of the current position.
        /// </summary>
        /// <remarks>
        /// If a selection persists, <see cref="removeSelection"/> must be called instead.
        /// </remarks>
        /// <returns>A string of the removed characters.</returns>
        private string removeCharacters(int number = 1)
        {
            if (Current.Disabled || text.Length == 0)
                return string.Empty;

            int removeStart = Math.Clamp(selectionRight - number, 0, selectionRight);
            int removeCount = selectionRight - removeStart;

            if (removeCount == 0)
                return string.Empty;

            Debug.Assert(selectionLength == 0 || removeCount == selectionLength);

            foreach (var d in TextFlow.Children.Skip(removeStart).Take(removeCount).ToArray()) //ToArray since we are removing items from the children in this block.
            {
                TextFlow.Remove(d);

                TextContainer.Add(d);

                // account for potentially altered height of textbox
                d.Y = TextFlow.BoundingBox.Y;

                d.Hide();
                d.Expire();
            }

            var removedText = text.Substring(removeStart, removeCount);
            text = text.Remove(removeStart, removeCount);

            // Reorder characters depth after removal to avoid ordering issues with newly added characters.
            for (int i = removeStart; i < TextFlow.Count; i++)
                TextFlow.ChangeChildDepth(TextFlow[i], getDepthForCharacterIndex(i));

            selectionStart = selectionEnd = removeStart;

            cursorAndLayout.Invalidate();

            return removedText;
        }

        /// <summary>
        /// Creates a single character. Override <see cref="Drawable.Show"/> and <see cref="Drawable.Hide"/> for custom behavior.
        /// </summary>
        /// <param name="c">The character that this <see cref="Drawable"/> should represent.</param>
        /// <returns>A <see cref="Drawable"/> that represents the character <paramref name="c"/> </returns>
        protected virtual Drawable GetDrawableCharacter(char c) => new SpriteText { Text = c.ToString(), Font = new FontUsage(size: CalculatedTextSize) };

        protected virtual Drawable AddCharacterToFlow(char c)
        {
            // Remove all characters to the right and store them in a local list,
            // such that their depth can be updated.
            List<Drawable> charsRight = new List<Drawable>();
            foreach (Drawable d in TextFlow.Children.Skip(selectionLeft))
                charsRight.Add(d);
            TextFlow.RemoveRange(charsRight);

            // Update their depth to make room for the to-be inserted character.
            int i = selectionLeft;
            foreach (Drawable d in charsRight)
                d.Depth = getDepthForCharacterIndex(i++);

            // Add the character
            Drawable ch = GetDrawableCharacter(c);
            ch.Depth = getDepthForCharacterIndex(selectionLeft);

            TextFlow.Add(ch);

            // Add back all the previously removed characters
            TextFlow.AddRange(charsRight);

            return ch;
        }

        private float getDepthForCharacterIndex(int index) => -index;

        protected float CalculatedTextSize => TextFlow.DrawSize.Y - (TextFlow.Padding.Top + TextFlow.Padding.Bottom);

        protected void InsertString(string value) => insertString(value);

        private void insertString(string value, Action<Drawable> drawableCreationParameters = null)
        {
            if (string.IsNullOrEmpty(value)) return;

            if (Current.Disabled)
            {
                NotifyInputError();
                return;
            }

            foreach (char c in value)
            {
                if (char.IsControl(c) || !CanAddCharacter(c))
                {
                    NotifyInputError();
                    continue;
                }

                if (selectionLength > 0)
                    removeSelection();

                if (text.Length + 1 > LengthLimit)
                {
                    NotifyInputError();
                    break;
                }

                Drawable drawable = AddCharacterToFlow(c);

                drawable.Show();
                drawableCreationParameters?.Invoke(drawable);

                text = text.Insert(selectionLeft, c.ToString());

                selectionStart = selectionEnd = selectionLeft + 1;

                cursorAndLayout.Invalidate();
            }
        }

        /// <summary>
        /// Called whenever an invalid character has been entered
        /// </summary>
        protected abstract void NotifyInputError();

        /// <summary>
        /// Invoked when new text is added via user input.
        /// </summary>
        /// <param name="added">The text which was added.</param>
        protected virtual void OnUserTextAdded(string added)
        {
        }

        /// <summary>
        /// Invoked when text is removed via user input.
        /// </summary>
        /// <param name="removed">The text which was removed.</param>
        protected virtual void OnUserTextRemoved(string removed)
        {
        }

        /// <summary>
        /// Invoked whenever a text string has been committed to the textbox.
        /// </summary>
        /// <param name="textChanged">Whether the current text string is different than the last committed.</param>
        protected virtual void OnTextCommitted(bool textChanged)
        {
        }

        /// <summary>
        /// Invoked whenever the caret has moved from its position.
        /// </summary>
        /// <param name="selecting">Whether the caret is selecting text while moving.</param>
        protected virtual void OnCaretMoved(bool selecting)
        {
        }

        /// <summary>
        /// Creates a placeholder that shows whenever the textbox is empty. Override <see cref="Drawable.Show"/> or <see cref="Drawable.Hide"/> for custom behavior.
        /// </summary>
        /// <returns>The placeholder</returns>
        protected abstract SpriteText CreatePlaceholder();

        protected SpriteText Placeholder;

        public string PlaceholderText
        {
            get => Placeholder.Text;
            set => Placeholder.Text = value;
        }

        protected abstract Caret CreateCaret();

        private readonly BindableWithCurrent<string> current = new BindableWithCurrent<string>();

        public Bindable<string> Current
        {
            get => current.Current;
            set => current.Current = value;
        }

        private string text = string.Empty;

        public virtual string Text
        {
            get => text;
            set
            {
                if (Current.Disabled)
                    return;

                if (value == text)
                    return;

                lastCommitText = value ??= string.Empty;

                if (value.Length == 0)
                    Placeholder.Show();
                else
                    Placeholder.Hide();

                if (!IsLoaded)
                    Current.Value = text = value;

                textUpdateScheduler.Add(delegate
                {
                    int startBefore = selectionStart;
                    selectionStart = selectionEnd = 0;
                    TextFlow?.Clear();

                    text = string.Empty;
                    InsertString(value);

                    selectionStart = Math.Clamp(startBefore, 0, text.Length);
                });

                cursorAndLayout.Invalidate();
            }
        }

        public string SelectedText => selectionLength > 0 ? Text.Substring(selectionLeft, selectionLength) : string.Empty;


        #region Input event handling

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (ReadOnly)
                return true;

            if (compositionActive)
                return true;

            switch (e.Key)
            {
                case Key.Escape:
                    KillFocus();
                    return true;

                case Key.KeypadEnter:
                case Key.Enter:
                    Commit();
                    return true;
            }

            return base.OnKeyDown(e);
        }

        /// <summary>
        /// Removes focus from this <see cref="TextBox"/> if it currently has focus.
        /// </summary>
        protected virtual void KillFocus() => killFocus();

        private string lastCommitText;

        private void killFocus()
        {
            var manager = GetContainingInputManager();
            if (manager.FocusedDrawable == this)
                manager.ChangeFocus(null);
        }

        /// <summary>
        /// Commits current text on this <see cref="TextBox"/> and releases focus if <see cref="ReleaseFocusOnCommit"/> is set.
        /// </summary>
        protected virtual void Commit()
        {
            if (ReleaseFocusOnCommit && HasFocus)
            {
                killFocus();
                if (CommitOnFocusLost)
                    // the commit will happen as a result of the focus loss.
                    return;
            }

            bool isNew = text != lastCommitText;
            lastCommitText = text;

            OnTextCommitted(isNew);
            OnCommit?.Invoke(this, isNew);
        }

        protected override void OnDrag(DragEvent e)
        {
            if (compositionActive)
                resetComposition();

            if (doubleClickWord != null)
            {
                //select words at a time
                if (getCharacterClosestTo(e.MousePosition) > doubleClickWord[1])
                {
                    selectionStart = doubleClickWord[0];
                    selectionEnd = findSeparatorIndex(text, getCharacterClosestTo(e.MousePosition) - 1, 1);
                    selectionEnd = selectionEnd >= 0 ? selectionEnd : text.Length;
                }
                else if (getCharacterClosestTo(e.MousePosition) < doubleClickWord[0])
                {
                    selectionStart = doubleClickWord[1];
                    selectionEnd = findSeparatorIndex(text, getCharacterClosestTo(e.MousePosition), -1);
                    selectionEnd = selectionEnd >= 0 ? selectionEnd + 1 : 0;
                }
                else
                {
                    //in the middle
                    selectionStart = doubleClickWord[0];
                    selectionEnd = doubleClickWord[1];
                }

                cursorAndLayout.Invalidate();
            }
            else
            {
                if (text.Length == 0) return;

                selectionEnd = getCharacterClosestTo(e.MousePosition);
                if (selectionLength > 0)
                    GetContainingInputManager().ChangeFocus(this);

                cursorAndLayout.Invalidate();
            }
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (HasFocus)
                return true;

            Vector2 posDiff = e.MouseDownPosition - e.MousePosition;

            return Math.Abs(posDiff.X) > Math.Abs(posDiff.Y);
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            if (compositionActive)
                resetComposition();

            if (text.Length == 0)
                return true;

            if (AllowClipboardExport)
            {
                int hover = Math.Min(text.Length - 1, getCharacterClosestTo(e.MousePosition));

                int lastSeparator = findSeparatorIndex(text, hover, -1);
                int nextSeparator = findSeparatorIndex(text, hover, 1);

                selectionStart = lastSeparator >= 0 ? lastSeparator + 1 : 0;
                selectionEnd = nextSeparator >= 0 ? nextSeparator : text.Length;
            }
            else
            {
                selectionStart = 0;
                selectionEnd = text.Length;
            }

            //in order to keep the home word selected
            doubleClickWord = new[] { selectionStart, selectionEnd };

            cursorAndLayout.Invalidate();
            return true;
        }

        private static int findSeparatorIndex(string input, int searchPos, int direction)
        {
            bool isLetterOrDigit = char.IsLetterOrDigit(input[searchPos]);

            for (int i = searchPos; i >= 0 && i < input.Length; i += direction)
            {
                if (char.IsLetterOrDigit(input[i]) != isLetterOrDigit)
                    return i;
            }

            return -1;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (compositionActive)
                resetComposition();

            selectionStart = selectionEnd = getCharacterClosestTo(e.MousePosition);

            cursorAndLayout.Invalidate();

            return false;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            doubleClickWord = null;
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            compositionActive = false;
            unbindInput();

            caret.Hide();
            cursorAndLayout.Invalidate();

            if (CommitOnFocusLost)
                Commit();
        }

        public override bool AcceptsFocus => true;

        protected override bool OnClick(ClickEvent e) => !ReadOnly;

        protected override void OnFocus(FocusEvent e)
        {
            bindInput();

            caret.Show();
            cursorAndLayout.Invalidate();
        }

        #endregion

        #region Native TextBox handling (platform-specific)

        private void unbindInput()
        {
            textInput?.Deactivate(this);
        }

        private void bindInput()
        {
            textInput?.Activate(this);
        }

        private bool compositionActive;

        private string prevComposition = String.Empty;

        private readonly List<Drawable> compositionDrawables = new List<Drawable>();

        private void resetComposition()
        {
            compositionActive = false;
            textInput?.StopTextComposition();
            Schedule(clearComposition);
        }

        private void handleTextInsert(string newText)
        {
            if (compositionActive)
            {
                Schedule(() =>
                {
                    clearComposition();
                    OnUserTextAdded(newText);
                    compositionActive = false;
                });
            }
            else
            {
                Schedule(() =>
                {
                    insertString(newText);
                    OnUserTextAdded(newText);
                });
            }
        }

        private void handleTextComposition(string newComposition)
        {
            // A new composition is handled on the Input thread (I think) before the rest of the OnKeyDown/OnPressed events.
            // As such, the composition is set to active first to be able check logic in the other event handlers as appropriate.
            // The actual text insertion is scheduled to run on the Update thread (I think).
            compositionActive = true;

            Schedule(() =>
            {
                // Adding new text to the current composition doesn't properly diff the compositionDrawables yet.
                // For now the behaviour is to just fully remove the previous composition and add the updated one.
                // This does look a little janky in BasicTextBox as the chars animate when removed.
                // Seems fine for now as a proof of concept.
                removeCharacters(prevComposition.Length);

                insertString(newComposition, d =>
                {
                    d.Colour = Color4.Aqua;
                    d.Alpha = 0.6f;
                    compositionDrawables.Add(d);
                });

                // The composition is no longer active if it's empty
                compositionActive = !String.IsNullOrEmpty(newComposition);
                prevComposition = newComposition;
            });
        }

        private void clearComposition()
        {
            if (compositionDrawables.Count > 0)
            {
                foreach (var d in compositionDrawables)
                {
                    d.Colour = Color4.White;
                    d.FadeTo(1, 200, Easing.Out);
                }
            }

            compositionDrawables.Clear();
            prevComposition = String.Empty;
        }


        #endregion
    }
}
