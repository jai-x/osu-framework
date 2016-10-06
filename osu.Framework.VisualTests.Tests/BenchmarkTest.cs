﻿using System;
using NUnit.Framework;
using osu.Framework.Platform;

namespace osu.Framework.VisualTests.Tests
{
    [TestFixture]
    public class BenchmarkTest
    {
        [Test]
        public void TestBenchmark()
        {
            BasicGameHost host = new HeadlessGameHost();
            host.Add(new Benchmark());
            host.Run();
        }
    }
}