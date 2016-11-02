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

using Microsoft.VisualStudio.VCProjectEngine;
using QtProjectLib;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QtVsTools
{
    public static class ExtLoader
    {
        public static void ImportProFile()
        {
            var vm = QtVersionManager.The();
            var qtVersion = vm.GetDefaultVersion();
            var qtDir = vm.GetInstallPath(qtVersion);
            if (qtDir == null) {
                Messages.DisplayErrorMessage(SR.GetString("CannotFindQMake"));
                return;
            }
            var vi = new VersionInformation(qtDir);
            if (vi.qtMajor < 5) {
                Messages.DisplayErrorMessage(SR.GetString("NoVSSupport"));
                return;
            }
            if (Vsix.Instance.Dte != null) {
                var proFileImporter = new ProjectImporter(Vsix.Instance.Dte);
                proFileImporter.ImportProFile(qtVersion);
            }
        }

        public static void ImportPriFile(EnvDTE.Project project)
        {
            if (project == null)
                return;

            VCProject vcproj;
            if (!HelperFunctions.IsQtProject(project))
                return;

            vcproj = project.Object as VCProject;
            if (vcproj == null)
                return;

            // make the user able to choose .pri file
            using (var fd = new OpenFileDialog()) {
                fd.Multiselect = false;
                fd.CheckFileExists = true;
                fd.Title = SR.GetString("ExportProject_ImportPriFile");
                fd.Filter = "Project Include Files (*.pri)|*.pri";
                fd.FileName = vcproj.ProjectDirectory + vcproj.Name + ".pri";

                if (fd.ShowDialog() != DialogResult.OK)
                    return;

                ImportPriFile(project, fd.FileName);
            }
        }

        public static void ImportPriFile(EnvDTE.Project project, string fileName)
        {
            if (project == null)
                return;

            VCProject vcproj;
            if (!HelperFunctions.IsQtProject(project))
                return;

            vcproj = project.Object as VCProject;
            if (vcproj == null)
                return;

            var vm = QtVersionManager.The();
            var qtDir = vm.GetInstallPath(vm.GetDefaultVersion());
            if (qtDir == null) {
                Messages.DisplayErrorMessage(SR.GetString("CannotFindQMake"));
                return;
            }

            var priFileInfo = new FileInfo(fileName);

            var qmake = new QMakeWrapper();
            qmake.setQtDir(qtDir);
            if (qmake.readFile(priFileInfo.FullName)) {
                var flat = qmake.isFlat();
                var priFiles = ResolveFilesFromQMake(qmake.sourceFiles(), project, priFileInfo.DirectoryName);
                var projFiles = HelperFunctions.GetProjectFiles(project, FilesToList.FL_CppFiles);
                projFiles = ProjectExporter.ConvertFilesToFullPath(projFiles, vcproj.ProjectDirectory);
                ProjectExporter.SyncIncludeFiles(vcproj, priFiles, projFiles, project.DTE, flat, Filters.SourceFiles());

                priFiles = ResolveFilesFromQMake(qmake.headerFiles(), project, priFileInfo.DirectoryName);
                projFiles = HelperFunctions.GetProjectFiles(project, FilesToList.FL_HFiles);
                projFiles = ProjectExporter.ConvertFilesToFullPath(projFiles, vcproj.ProjectDirectory);
                ProjectExporter.SyncIncludeFiles(vcproj, priFiles, projFiles, project.DTE, flat, Filters.HeaderFiles());

                priFiles = ResolveFilesFromQMake(qmake.formFiles(), project, priFileInfo.DirectoryName);
                projFiles = HelperFunctions.GetProjectFiles(project, FilesToList.FL_UiFiles);
                projFiles = ProjectExporter.ConvertFilesToFullPath(projFiles, vcproj.ProjectDirectory);
                ProjectExporter.SyncIncludeFiles(vcproj, priFiles, projFiles, project.DTE, flat, Filters.FormFiles());

                priFiles = ResolveFilesFromQMake(qmake.resourceFiles(), project, priFileInfo.DirectoryName);
                projFiles = HelperFunctions.GetProjectFiles(project, FilesToList.FL_Resources);
                projFiles = ProjectExporter.ConvertFilesToFullPath(projFiles, vcproj.ProjectDirectory);
                ProjectExporter.SyncIncludeFiles(vcproj, priFiles, projFiles, project.DTE, flat, Filters.ResourceFiles());
            } else {
                Messages.PaneMessage(project.DTE, "--- (Importing .pri file) file: "
                    + priFileInfo + " could not be read.");
            }
        }

        private static List<string> ResolveFilesFromQMake(string[] files, EnvDTE.Project project, string path)
        {
            var lst = new List<string>();
            foreach (var file in files) {
                var s = ResolveEnvironmentVariables(file, project);
                if (s == null) {
                    Messages.PaneMessage(project.DTE, SR.GetString("ImportPriFileNotResolved", file));
                } else {
                    if (!HelperFunctions.IsAbsoluteFilePath(s))
                        s = path + "\\" + s;
                    lst.Add(s);
                }
            }
            return lst;
        }

        private static string ResolveEnvironmentVariables(string str, EnvDTE.Project project)
        {
            string env = null;
            string val = null;
            var reg = new Regex(@"\$\(([^\s\(\)]+)\)");
            var col = reg.Matches(str);
            for (var i = 0; i < col.Count; ++i) {
                env = col[i].Groups[1].ToString();
                if (env == "QTDIR") {
                    var vm = QtVersionManager.The();
                    val = vm.GetInstallPath(project);
                    if (val == null)
                        val = System.Environment.GetEnvironmentVariable(env);
                } else {
                    val = System.Environment.GetEnvironmentVariable(env);
                }
                if (val == null)
                    return null;
                str = str.Replace("$(" + env + ")", val);
            }
            return str;
        }

        public static void ExportProFile()
        {
            if (Vsix.Instance.Dte != null) {
                var proFileExporter = new ProjectExporter(Vsix.Instance.Dte);
                proFileExporter.ExportToProFile();
            }
        }

        public static void ExportPriFile()
        {
            var dte = Vsix.Instance.Dte;
            if (dte != null) {
                var proFileExporter = new ProjectExporter(dte);
                proFileExporter.ExportToPriFile(HelperFunctions.GetSelectedQtProject
                    (dte));
            }
        }
    }
}
