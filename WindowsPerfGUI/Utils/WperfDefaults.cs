﻿// BSD 3-Clause License
//
// Copyright (c) 2024, Arm Limited
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// 1. Redistributions of source code must retain the above copyright notice, this
//    list of conditions and the following disclaimer.
//
// 2. Redistributions in binary form must reproduce the above copyright notice,
//    this list of conditions and the following disclaimer in the documentation
//    and/or other materials provided with the distribution.
//
// 3. Neither the name of the copyright holder nor the names of its
//    contributors may be used to endorse or promote products derived from
//    this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

namespace WindowsPerfGUI.Utils
{
    static class WperfDefaults
    {
        public const string WPERF_MIN_VERSION = "3.8.0";
        public static bool HasSPESupport = false;
        public static string SPEFeatureName = "";
#nullable enable
        public static string? Frequency;
        public static int? TotalGPCNum;
        public static int? CoreNum = null;

#nullable disable

        public static string DefaultWPASearchDir =
            Environment.GetEnvironmentVariable("WPA_ADDITIONAL_SEARCH_DIRECTORIES")
            ?? "C:\\Program Files (x86)\\Windows Kits\\10\\Windows Performance Toolkit\\CustomDataSources";

        public static string DefaultWperfPath = Environment.GetEnvironmentVariable(
            "WINDOWSPERF_PATH"
        );

        public static string DefaultWperfExecutable = "wperf.exe";
    }
}
