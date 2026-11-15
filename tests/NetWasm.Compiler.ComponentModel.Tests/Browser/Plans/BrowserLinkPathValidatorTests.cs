// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkPathValidatorTests
{
    [Theory]
    [InlineData("application.wasm")]
    [InlineData("/netwasm-link/raw/linked.wasm")]
    [InlineData("work/.owned_module-1.wasm")]
    public void AcceptsNormalizedLogicalPathsWithoutHostResolution(string path) => Create().Validate(path);

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/")]
    [InlineData("//module")]
    [InlineData("work/")]
    [InlineData("work//module")]
    [InlineData("./module")]
    [InlineData("work/../module")]
    [InlineData("work/./module")]
    [InlineData("--output")]
    [InlineData("/-output")]
    [InlineData("C:/module")]
    [InlineData("work\\module")]
    [InlineData("module?query")]
    [InlineData("module\n")]
    public void RejectsAmbiguousOrNoncanonicalPaths(string path) =>
        Assert.Throws<ArgumentException>(() => Create().Validate(path));

    [Fact]
    public void RejectsNullPath() => Assert.Throws<ArgumentNullException>(() => Create().Validate(null!));

    private static IBrowserLinkPathValidator Create() =>
        Assert.IsAssignableFrom<IBrowserLinkPathValidator>(new BrowserLinkPathValidator());
}
