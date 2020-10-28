// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osuTK;

namespace osu.Framework.Tests.Visual.UserInterface
{
    public class TestSceneTextBoxIme : ManualInputManagerTestScene
    {
        [SetUp]
        public new void SetUp() => Schedule(() =>
        {
            Add(new BasicTextBox
            {
                Size = new Vector2(600, 40),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            });
        });
    }
}
