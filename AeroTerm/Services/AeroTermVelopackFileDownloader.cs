// <copyright file="AeroTermVelopackFileDownloader.cs">
// Copyright (c) AeroTerm Developers. All rights reserved.
// Licensed under the GPLv2 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace AeroTerm.Services;

using System.Net;
using System.Net.Http;
using Velopack.Sources;

/// <summary>
/// Custom Velopack <see cref="HttpClientFileDownloader"/> that disables
/// transparent HTTP response decompression.
/// </summary>
/// <remarks>
/// The default Velopack downloader enables
/// <see cref="DecompressionMethods.GZip"/> + <see cref="DecompressionMethods.Deflate"/>.
/// .NET's <see cref="HttpClient"/> strips the <c>Content-Length</c> response
/// header when it transparently decompresses a response, because the header
/// reflects the compressed length, not the decompressed bytes the caller
/// reads. Velopack's <c>DownloadToStreamInternal</c> only reports per-chunk
/// progress when <c>Content-Length</c> is known; otherwise it falls back to
/// <see cref="System.IO.Stream.CopyToAsync(System.IO.Stream)"/> and emits
/// no intermediate progress at all — causing the AeroTerm update progress
/// bar to jump straight from 0 to 100. Disabling automatic decompression
/// preserves <c>Content-Length</c>; release <c>.nupkg</c> files are binary
/// and not meaningfully compressible, so the trade-off is essentially free.
/// </remarks>
internal sealed class AeroTermVelopackFileDownloader : HttpClientFileDownloader
{
    /// <inheritdoc/>
    protected override HttpClientHandler CreateHttpClientHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.None,
        };
    }
}
