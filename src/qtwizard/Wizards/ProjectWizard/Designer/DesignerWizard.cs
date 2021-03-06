/****************************************************************************
**
** Copyright (C) 2016 The Qt Company Ltd.
** Contact: https://www.qt.io/licensing/
**
** This file is part of the Qt VS Tools.
**
** $QT_BEGIN_LICENSE:GPL-EXCEPT$
** Commercial License Usage
** Licensees holding valid commercial Qt licenses may use this file in
** accordance with the commercial license agreement provided with the
** Software or, alternatively, in accordance with the terms contained in
** a written agreement between you and The Qt Company. For licensing terms
** and conditions see https://www.qt.io/terms-conditions. For further
** information use the contact form at https://www.qt.io/contact-us.
**
** GNU General Public License Usage
** Alternatively, this file may be used under the terms of the GNU
** General Public License version 3 as published by the Free Software
** Foundation with exceptions as appearing in the file LICENSE.GPL3-EXCEPT
** included in the packaging of this file. Please review the following
** information to ensure the GNU General Public License requirements will
** be met: https://www.gnu.org/licenses/gpl-3.0.html.
**
** $QT_END_LICENSE$
**
****************************************************************************/

using EnvDTE;
using Microsoft.Internal.VisualStudio.PlatformUI;
using IServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TemplateWizard;
using Microsoft.VisualStudio.VCProjectEngine;
using QtProjectLib;
using QtVsTools.VisualStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Forms;

namespace QtVsTools.Wizards.ProjectWizard
{
    public class DesignerWizard : IWizard
    {
        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }

        public void ProjectFinishedGenerating(Project project)
        {
            var qtProject = QtProject.Create(project);

            QtVSIPSettings.SaveUicDirectory(project, null);
            QtVSIPSettings.SaveMocDirectory(project, null);
            QtVSIPSettings.SaveMocOptions(project, null);
            QtVSIPSettings.SaveRccDirectory(project, null);
            QtVSIPSettings.SaveLUpdateOnBuild(project);
            QtVSIPSettings.SaveLUpdateOptions(project, null);
            QtVSIPSettings.SaveLReleaseOptions(project, null);

            var vm = QtVersionManager.The();
            var qtVersion = vm.GetDefaultVersion();
            var vi = VersionInformation.Get(vm.GetInstallPath(qtVersion));
            if (vi.GetVSPlatformName() != "Win32")
                qtProject.SelectSolutionPlatform(vi.GetVSPlatformName());

            qtProject.MarkAsQtProject();
            qtProject.AddDirectories();

            var type = TemplateType.PluginProject | TemplateType.DynamicLibrary | TemplateType.GUISystem;
            qtProject.WriteProjectBasicConfigurations(type, data.UsePrecompiledHeader);

            var vcProject = qtProject.VCProject;
            var files = vcProject.GetFilesWithItemType(@"None") as IVCCollection;
            foreach (var vcFile in files)
                vcProject.RemoveFile(vcFile);

            if (data.UsePrecompiledHeader) {
                qtProject.AddFileToProject(@"stdafx.cpp", Filters.SourceFiles());
                qtProject.AddFileToProject(@"stdafx.h", Filters.HeaderFiles());
            }

            qtProject.AddFileToProject(data.ClassSourceFile, Filters.SourceFiles());
            qtProject.AddFileToProject(data.ClassHeaderFile, Filters.HeaderFiles());

            qtProject.AddFileToProject(data.PluginSourceFile, Filters.SourceFiles());
            qtProject.AddFileToProject(data.PluginHeaderFile, Filters.HeaderFiles());

            qtProject.AddFileToProject(data.PluginClass.ToLower() + @".json", Filters.OtherFiles());

            foreach (VCFile file in (IVCCollection) qtProject.VCProject.Files)
                qtProject.AdjustWhitespace(file.FullPath);

            qtProject.SetQtEnvironment(qtVersion);
            qtProject.Finish(); // Collapses all project nodes.
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
        }

        public void RunFinished()
        {
        }

        public void RunStarted(object automation, Dictionary<string, string> replacements,
            WizardRunKind runKind, object[] customParams)
        {
            var qtMoc = new StringBuilder();
            var serviceProvider = new ServiceProvider(automation as IServiceProvider);
            var iVsUIShell = VsServiceProvider.GetService<SVsUIShell, IVsUIShell>();

            iVsUIShell.EnableModeless(0);

            var versionMgr = QtVersionManager.The();
            var versionName = versionMgr.GetDefaultVersion();
            var versionInfo = VersionInformation.Get(versionMgr.GetInstallPath(versionName));
            if (versionInfo.isWinRT()) {
                MessageBox.Show(
                    string.Format(
                        "The Qt Custom Designer Widget project type is not available\r\n" +
                        "for the currently selected Qt version ({0}).", versionName),
                    "Project Type Not Available", MessageBoxButtons.OK, MessageBoxIcon.Error);
                iVsUIShell.EnableModeless(1);
                throw new WizardBackoutException();
            }

            try {
                System.IntPtr hwnd;
                iVsUIShell.GetDialogOwnerHwnd(out hwnd);

                try {
                    var className = replacements["$safeprojectname$"];
                    className = Regex.Replace(className, @"[^a-zA-Z0-9_]", string.Empty);
                    className = Regex.Replace(className, @"^[\d-]*\s*", string.Empty);
                    className = Regex.Replace(className, @"[pP][lL][uU][gG][iI][nN]$", string.Empty);
                    var result = new Util.ClassNameValidationRule().Validate(className, null);
                    if (result != ValidationResult.ValidResult)
                        className = @"MyDesignerWidget";

                    data.ClassName = className;
                    data.BaseClass = @"QWidget";
                    data.ClassHeaderFile = className + @".h";
                    data.ClassSourceFile = className + @".cpp";
                    data.PluginClass = className + @"Plugin";
                    data.PluginHeaderFile = data.PluginClass + @".h";
                    data.PluginSourceFile = data.PluginClass + @".cpp";

                    var wizard = new WizardWindow(new List<WizardPage> {
                        new WizardIntroPage {
                            Data = data,
                            Header = @"Welcome to the Qt Custom Designer Widget",
                            Message = @"This wizard generates a custom designer widget which can be "
                                + @"used in Qt Designer or Visual Studio."
                                + System.Environment.NewLine + System.Environment.NewLine
                                + "To continue, click Next.",
                            PreviousButtonEnabled = false,
                            NextButtonEnabled = true,
                            FinishButtonEnabled = false,
                            CancelButtonEnabled = true
                        },
                        new ModulePage {
                            Data = data,
                            Header = @"Welcome to the Qt Custom Designer Widget",
                            Message = @"Select the modules you want to include in your project. The "
                                + @"recommended modules for this project are selected by default.",
                            PreviousButtonEnabled = true,
                            NextButtonEnabled = true,
                            FinishButtonEnabled = false,
                            CancelButtonEnabled = true
                        },
                        new DesignerPage {
                            Data = data,
                            Header = @"Welcome to the Qt Custom Designer Widget",
                            Message = @"This wizard generates a custom designer widget which can be "
                                + @"used in Qt Designer or Visual Studio.",
                            PreviousButtonEnabled = true,
                            NextButtonEnabled = false,
                            FinishButtonEnabled = data.DefaultModules.All(QtModuleInfo.IsInstalled),
                            CancelButtonEnabled = true
                        }
                    })
                    {
                        Title = @"Qt Custom Designer Widget"
                    };
                    WindowHelper.ShowModal(wizard, hwnd);
                    if (!wizard.DialogResult.HasValue || !wizard.DialogResult.Value)
                        throw new System.Exception("Unexpected wizard return value.");
                } catch (QtVSException exception) {
                    Messages.DisplayErrorMessage(exception.Message);
                    throw; // re-throw, but keep the original exception stack intact
                }

                var version = (automation as DTE).Version;
                replacements["$ToolsVersion$"] = version;

                replacements["$Platform$"] = versionInfo.GetVSPlatformName();

                replacements["$Keyword$"] = Resources.qtProjectKeyword;
                replacements["$ProjectGuid$"] = HelperFunctions.NewProjectGuid();
                replacements["$PlatformToolset$"] = BuildConfig.PlatformToolset(version);
                replacements["$DefaultQtVersion$"] = versionName;
                replacements["$QtModules$"] = string.Join(";", data.Modules
                    .Select(moduleName => QtModules.Instance
                        .ModuleInformation(QtModules.Instance
                        .ModuleIdByName(moduleName))
                        .proVarQT)
                    .Union(new[] { "designer " })
                    .ToDictionary(x => x, StringComparer.InvariantCultureIgnoreCase).Keys);

                replacements["$classname$"] = data.ClassName;
                replacements["$baseclass$"] = data.BaseClass;
                replacements["$sourcefilename$"] = data.ClassSourceFile;
                replacements["$headerfilename$"] = data.ClassHeaderFile;

                replacements["$plugin_class$"] = data.PluginClass;
                replacements["$pluginsourcefilename$"] = data.PluginSourceFile;
                replacements["$pluginheaderfilename$"] = data.PluginHeaderFile;

                replacements["$plugin_json$"] = data.PluginClass.ToLower() + @".json";
                replacements["$objname$"] = char.ToLower(data.ClassName[0]) + data.ClassName
                    .Substring(1);

                replacements["$precompiledheader$"] = string.Empty;
                replacements["$precompiledsource$"] = string.Empty;

                var strHeaderInclude = data.ClassHeaderFile;
                if (data.UsePrecompiledHeader) {
                    strHeaderInclude = "stdafx.h\"\r\n#include \"" + data.ClassHeaderFile;
                    replacements["$precompiledheader$"] = "<None Include=\"stdafx.h\" />";
                    replacements["$precompiledsource$"] = "<None Include=\"stdafx.cpp\" />";
                    qtMoc.Append("<PrependInclude>stdafx.h</PrependInclude>");
                }
                replacements["$include$"] = strHeaderInclude;

#if (VS2019 || VS2017 || VS2015)
                string versionWin10SDK = HelperFunctions.GetWindows10SDKVersion();
                if (!string.IsNullOrEmpty(versionWin10SDK)) {
                    replacements["$WindowsTargetPlatformVersion$"] = versionWin10SDK;
                    replacements["$isSet_WindowsTargetPlatformVersion$"] = "true";
                }

                if (qtMoc.Length > 0)
                    replacements["$QtMoc$"] = string.Format("<QtMoc>{0}</QtMoc>", qtMoc);
                else
                    replacements["$QtMoc$"] = string.Empty;
#endif
            } catch {
                try {
                    Directory.Delete(replacements["$destinationdirectory$"]);
                    Directory.Delete(replacements["$solutiondirectory$"]);
                } catch { }

                iVsUIShell.EnableModeless(1);
                throw new WizardBackoutException();
            }

            iVsUIShell.EnableModeless(1);
        }

        public bool ShouldAddProjectItem(string filePath)
        {
            return true;
        }

        private readonly WizardData data = new WizardData
        {
            DefaultModules = new List<string> {
                @"QtCore", @"QtGui", @"QtWidgets", @"QtXml"
            }
        };
    }
}
