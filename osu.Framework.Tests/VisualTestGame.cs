// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Framework.IO.Stores;

using osu.Game.Resources; // for CJK fonts

namespace osu.Framework.Tests
{
    internal class VisualTestGame : TestGame
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));

            AddFont(Resources, @"Fonts/Noto-Basic");
            AddFont(Resources, @"Fonts/Noto-CJK-Basic");

            Child = new SafeAreaContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new DrawSizePreservingFillContainer
                {
                    Children = new Drawable[]
                    {
                        new TestBrowser(),
                        new CursorContainer(),
                    },
                }
            };
        }

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);
            host.Window.CursorState |= CursorState.Hidden;
        }
    }
}
