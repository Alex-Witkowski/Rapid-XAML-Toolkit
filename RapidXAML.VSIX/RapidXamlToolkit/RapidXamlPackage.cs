﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using RapidXamlToolkit.Analyzers;
using RapidXamlToolkit.Commands;
using RapidXamlToolkit.Logging;
using RapidXamlToolkit.Options;
using RapidXamlToolkit.Telemetry;
using Task = System.Threading.Tasks.Task;

namespace RapidXamlToolkit
{
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids.SolutionHasMultipleProjects)]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids.SolutionHasSingleProject)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideOptionPage(typeof(SettingsConfigPage), "RapidXAML", "Profiles", 106, 107, true)]
    public sealed class RapidXamlPackage : AsyncPackage
    {
        public const string PackageGuidString = "c735dfc3-c416-4501-bc33-558e2aaad8c5";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var rxtLogger = new RxtLogger();

            var telemKey = string.Empty;

            var telemLogger = TelemetryAccessor.Create(rxtLogger, telemKey);

            var logger = new RxtLoggerWithTelemtry(rxtLogger, telemLogger);

            try
            {
                // Set the ServiceProvider of AnalyzerBase as it's needed to get settings
                AnalyzerBase.ServiceProvider = this;
                logger.RecordInfo($"Initializing Commands (v{CoreDetails.GetVersion()})");

                await CreateViewCommand.InitializeAsync(this, logger);
                await CopyToClipboardCommand.InitializeAsync(this, logger);
                await SendToToolboxCommand.InitializeAsync(this, logger);
                await OpenOptionsCommand.InitializeAsync(this, logger);
                await SetDatacontextCommand.InitializeAsync(this, logger);
            }
            catch (Exception exc)
            {
                logger.RecordException(exc);
                throw;
            }
        }
    }
}
