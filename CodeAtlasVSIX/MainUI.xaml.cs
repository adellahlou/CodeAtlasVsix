﻿using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CodeAtlasVSIX
{
    /// <summary>
    /// MainUI.xaml 的交互逻辑
    /// </summary>
    public partial class MainUI : DockPanel
    {
        List<KeyBinding> m_keyCommands = new List<KeyBinding>();
        Package m_package;
        // Switch for all commands
        bool m_isCommandEnable = true;

        public MainUI()
        {
            InitializeComponent();

            ResourceSetter resMgr = new ResourceSetter(this);
            resMgr.SetStyle();

            analysisWindow.InitLanguageOption();

            AddCommand(OnFindCallers, Key.C);
            AddCommand(OnFindCallees, Key.V);
            AddCommand(OnFindMembers, Key.M);
            AddCommand(OnFindOverrides, Key.O);
            AddCommand(OnFindBases, Key.B);
            AddCommand(OnFindUses, Key.U);
            AddCommand(OnGoUp, Key.Up);
            AddCommand(OnGoDown, Key.Down);
            AddCommand(OnGoLeft, Key.Left);
            AddCommand(OnGoRight, Key.Right);
            AddCommand(OnDelectSelectedItems, Key.Delete, ModifierKeys.Alt);
            AddCommand(OnDeleteSelectedItemsAndAddToStop, Key.I);
            AddCommand(OnAddSimilarCodeItem, Key.S);
            AddCommand(OnToggleScheme1, Key.D1, ModifierKeys.Control);
            AddCommand(OnToggleScheme2, Key.D2, ModifierKeys.Control);
            AddCommand(OnToggleScheme3, Key.D3, ModifierKeys.Control);
            AddCommand(OnToggleScheme4, Key.D4, ModifierKeys.Control);
            AddCommand(OnToggleScheme5, Key.D5, ModifierKeys.Control);
            AddCommand(OnShowScheme1, Key.D1, ModifierKeys.Alt);
            AddCommand(OnShowScheme2, Key.D2, ModifierKeys.Alt);
            AddCommand(OnShowScheme3, Key.D3, ModifierKeys.Alt);
            AddCommand(OnShowScheme4, Key.D4, ModifierKeys.Alt);
            AddCommand(OnShowScheme5, Key.D5, ModifierKeys.Alt);
        }

        public void SetCommandActive(bool isActive)
        {
            m_isCommandEnable = isActive;
            schemeWindow.Dispatcher.Invoke((ThreadStart)delegate
            {
                this.mask.Visibility = isActive ? Visibility.Hidden : Visibility.Visible;
                this.searchWindow.IsEnabled = isActive;
                this.symbolWindow.IsEnabled = isActive;
                this.schemeWindow.IsEnabled = isActive;
                this.tabControl.IsEnabled = isActive;
                this.menu.IsEnabled = isActive;
            });
        }

        public bool GetCommandActive()
        {
            return m_isCommandEnable;
        }

        public void SetPackage(Package package)
        {
            m_package = package;
        }

        public void AddCommand(ExecutedRoutedEventHandler callback, Key key, ModifierKeys modifier = ModifierKeys.Alt)
        {
            CommandBinding cmd = new CommandBinding();
            cmd.Command = new RoutedUICommand();

            cmd.Executed += callback;
            cmd.CanExecute += new CanExecuteRoutedEventHandler(_AlwaysCanExecute);
            this.CommandBindings.Add(cmd);
            KeyBinding CmdKey = new KeyBinding();
            CmdKey.Key = key;
            CmdKey.Modifiers = modifier;
            CmdKey.Command = cmd.Command;
            this.InputBindings.Add(CmdKey);
            m_keyCommands.Add(CmdKey);
        }

        private void _AlwaysCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        public void OnOpen(object sender, RoutedEventArgs e)
        {
            if (!GetCommandActive())
            {
                return;
            }
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
            if (dte != null)
            {
                Solution solution = dte.Solution;
                var solutionFile = solution.FileName;
                if (solutionFile != "")
                {
                    var doxyFolder = System.IO.Path.GetDirectoryName(solutionFile) + "\\CodeGraphData";
                    CheckOrCreateFolder(doxyFolder);
                    ofd.InitialDirectory = doxyFolder;
                }
            }
            ofd.DefaultExt = ".graph";
            ofd.Filter = "Code Graph Analysis Result|*.graph";
            if (ofd.ShowDialog() == true)
            {
                if (DBManager.Instance().GetDB().IsOpen())
                {
                    DBManager.Instance().CloseDB();
                }
                DBManager.Instance().OpenDB(ofd.FileName);
                UpdateUI();
            }
        }

        void CheckOrCreateFolder(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private void TestButton_Click(object sender, RoutedEventArgs e)
        {
            // defaultPath = r"C:\Users\me\AppData\Roaming\Sublime Text 3\Packages\CodeAtlas\CodeAtlasSublime.udb"
            // defaultPath = "I:/Programs/masteringOpenCV/Chapter3_MarkerlessAR/doc/xml/index.xml"
            var defaultPath = "I:/Programs/mitsuba/Doxyfile";
            //var defaultPath = "D:/Code/NewRapidRT/rapidrt/Doxyfile";

            var newDownTime = DateTime.Now;
            DBManager.Instance().OpenDB(defaultPath);
            double duration = (DateTime.Now - newDownTime).TotalSeconds;
            Logger.Debug("open time:" + duration.ToString());
            UpdateUI();
        }

        public void OnClose(object sender, RoutedEventArgs e)
        {
            if (!GetCommandActive())
            {
                return;
            }
            DBManager.Instance().CloseDB();
            UpdateUI();
            UIManager.Instance().GetScene().m_selectTimeStamp += 1;
        }

        public void UpdateUI()
        {
            schemeWindow.Dispatcher.Invoke((ThreadStart)delegate
            {
                schemeWindow.UpdateScheme();
            });
            symbolWindow.Dispatcher.Invoke((ThreadStart)delegate
            {
                symbolWindow.UpdateForbiddenSymbol();
                symbolWindow.UpdateSymbol("", "");
            });
            symbolWindow.Dispatcher.Invoke((ThreadStart)delegate
            {
                searchWindow.OnSearch();
            });
            analysisWindow.Dispatcher.Invoke((ThreadStart)delegate
            {
                analysisWindow.UpdateExtensionList();
            });
        }

        void GetCursorWord(EnvDTE.TextSelection ts, out string name, out string longName, out int lineNum)
        {
            name = null;
            longName = null;
            lineNum = ts.AnchorPoint.Line;
            int lineOffset = ts.AnchorPoint.LineCharOffset;

            // If a line of text is selected, used as search word.
            var cursorStr = ts.Text.Trim();
            if (cursorStr.Length != 0 && cursorStr.IndexOf('\n') == -1)
            {
                name = cursorStr;
                longName = cursorStr;
                return;
            }

            // Otherwise, use the word under the cursor.
            ts.SelectLine();
            string lineText = ts.Text;
            ts.MoveToLineAndOffset(lineNum, lineOffset);

            Regex rx = new Regex(@"\b(?<word>\w+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            MatchCollection matches = rx.Matches(lineText);

            // Report on each match
            int lastStartIndex = 0;
            int lastEndIndex = 0;
            for (int ithMatch = 0; ithMatch < matches.Count; ++ithMatch)
            {
                var match = matches[ithMatch];
                string word = match.Groups["word"].Value;
                int startIndex = match.Groups["word"].Index;
                int endIndex = startIndex + word.Length;
                int lineIndex = lineOffset - 1;
                if (startIndex <= lineIndex && endIndex >= lineIndex)
                {
                    name = word;
                    longName = word;
                    var midStr = lineText.Substring(lastEndIndex, (startIndex - lastEndIndex));
                    if (ithMatch > 0 && midStr == "::")
                    {
                        longName = lineText.Substring(lastStartIndex, (endIndex - lastStartIndex));
                        break;
                    }
                }
                lastStartIndex = startIndex;
                lastEndIndex = endIndex;
            }
        }

        public void OnShowInAtlas(object sender, ExecutedRoutedEventArgs e)
        {
            if (!GetCommandActive())
            {
                return;
            }

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
            Document doc = null;
            if (dte != null)
            {
                doc = dte.ActiveDocument;
            }
            if (doc == null)
            {
                return;
            }
            string token = null;
            string longName = null;
            int lineNum = 0;
            EnvDTE.TextSelection ts = doc.Selection as EnvDTE.TextSelection;
            GetCursorWord(ts, out token, out longName, out lineNum);

            var searched = false;
            if (token != null)
            {
                // Search token under cursor
                string docPath = doc.FullName;
                searched = SearchAndAddToScene(token, (int)DoxygenDB.SearchOption.MATCH_CASE|(int)DoxygenDB.SearchOption.MATCH_WORD,
                    longName, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.DB_CONTAINS_WORD,
                    "", docPath, lineNum);
                if (!searched)
                {
                    searched = SearchAndAddToScene(token, (int)DoxygenDB.SearchOption.MATCH_WORD,
                        longName, (int)DoxygenDB.SearchOption.DB_CONTAINS_WORD,
                        "", docPath, lineNum);
                }
            }
            else
            {
                // Search parent scope
                var navigator = new CursorNavigator();
                Document cursorDoc;
                CodeElement element;
                int line;
                navigator.GetCursorElement(out cursorDoc, out element, out line);
                if (element != null && cursorDoc != null)
                {
                    var kind = VSElementTypeToString(element);
                    searched = SearchAndAddToScene(
                        element.Name, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.MATCH_WORD, 
                        element.FullName, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.DB_CONTAINS_WORD,
                        kind, cursorDoc.FullName, lineNum);
                }
            }
            // Search the whole document
            if (!searched && token == null)
            {
                var fileName = doc.Name;
                var fullPath = doc.FullName.Replace("\\","/");
                SearchAndAddToScene(fileName, (int)DoxygenDB.SearchOption.MATCH_WORD,
                    fileName, (int)DoxygenDB.SearchOption.DB_CONTAINS_WORD,
                    "file", fullPath, 0);
            }
        }

        bool SearchAndAddToScene(
            string name, int nameOption,
            string longName, int longNameOption,
            string type, string docPath, int lineNum)
        {
            searchWindow.nameEdit.Text = name;
            searchWindow.typeEdit.Text = type;
            searchWindow.fileEdit.Text = docPath;
            searchWindow.lineEdit.Text = lineNum.ToString();

            searchWindow.resultList.Items.Clear();
            var db = DBManager.Instance().GetDB();
            if (db == null)
            {
                return false;
            }

            var request = new DoxygenDB.EntitySearchRequest(
                name, nameOption,
                longName, longNameOption,
                type, docPath, lineNum);
            var result = new DoxygenDB.EntitySearchResult();
            db.SearchAndFilter(request, out result);
            searchWindow.SetSearchResult(result);
            searchWindow.OnAddToScene();
            return result.candidateList.Count != 0;
        }

        public void OnShowDefinitionInAtlas(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();

            CodeElement srcElement, tarElement;
            Document srcDocument, tarDocument;
            int srcLine, tarLine;
            var navigator = new CursorNavigator();
            navigator.GetCursorElement(out srcDocument, out srcElement, out srcLine);

            Guid cmdGroup = VSConstants.GUID_VSStandardCommandSet97;
            var commandTarget = ((System.IServiceProvider)m_package).GetService(typeof(SUIHostCommandDispatcher)) as IOleCommandTarget;
            if (commandTarget != null)
            {
                int hr = commandTarget.Exec(ref cmdGroup,
                                             (uint)VSConstants.VSStd97CmdID.GotoDefn,
                                             (uint)OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT,
                                             System.IntPtr.Zero, System.IntPtr.Zero);

            }

            navigator.GetCursorElement(out tarDocument, out tarElement, out tarLine);
            if (srcElement == null || tarElement == null || srcElement == tarElement)
            {
                return;
            }

            var srcName = srcElement.Name;
            var srcLongName = srcElement.FullName;
            var srcType = VSElementTypeToString(srcElement);
            var srcFile = srcDocument.FullName;

            var tarName = tarElement.Name;
            var tarLongName = tarElement.FullName;
            var tarType = VSElementTypeToString(tarElement);
            var tarFile = tarDocument.FullName;

            var db = DBManager.Instance().GetDB();
            var srcRequest = new DoxygenDB.EntitySearchRequest(
                srcName, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.MATCH_WORD,
                srcLongName, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.DB_CONTAINS_WORD,
                srcType, srcFile, srcLine);
            var srcResult = new DoxygenDB.EntitySearchResult();
            db.SearchAndFilter(srcRequest, out srcResult);

            var tarRequest = new DoxygenDB.EntitySearchRequest(
                tarName, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.MATCH_WORD,
                tarLongName, (int)DoxygenDB.SearchOption.MATCH_CASE | (int)DoxygenDB.SearchOption.DB_CONTAINS_WORD,
                tarType, tarFile, tarLine);
            var tarResult = new DoxygenDB.EntitySearchResult();
            db.SearchAndFilter(tarRequest, out tarResult);

            DoxygenDB.Entity srcBestEntity, tarBestEntity;
            srcBestEntity = srcResult.bestEntity;
            tarBestEntity = tarResult.bestEntity;
            if (srcBestEntity != null && tarBestEntity != null && srcBestEntity.m_id != tarBestEntity.m_id)
            {
                scene.AcquireLock();
                scene.AddCodeItem(srcBestEntity.m_id);
                scene.AddCodeItem(tarBestEntity.m_id);
                scene.DoAddCustomEdge(srcBestEntity.m_id, tarBestEntity.m_id);
                scene.SelectCodeItem(tarBestEntity.m_id);
                scene.ReleaseLock();
            }
        }

        string VSElementTypeToString(CodeElement element)
        {
            string typeStr = "";
            var type = element.Kind;
            switch (type)
            {
                case vsCMElement.vsCMElementOther:
                    break;
                case vsCMElement.vsCMElementClass:
                    typeStr = "class";
                    break;
                case vsCMElement.vsCMElementFunction:
                    typeStr = "function";
                    break;
                case vsCMElement.vsCMElementVariable:
                    typeStr = "variable";
                    break;
                case vsCMElement.vsCMElementProperty:
                    break;
                case vsCMElement.vsCMElementNamespace:
                    typeStr = "namespace";
                    break;
                case vsCMElement.vsCMElementParameter:
                    break;
                case vsCMElement.vsCMElementAttribute:
                    break;
                case vsCMElement.vsCMElementInterface:
                    typeStr = "function";
                    break;
                case vsCMElement.vsCMElementDelegate:
                    break;
                case vsCMElement.vsCMElementEnum:
                    break;
                case vsCMElement.vsCMElementStruct:
                    typeStr = "struct";
                    break;
                case vsCMElement.vsCMElementUnion:
                    break;
                case vsCMElement.vsCMElementLocalDeclStmt:
                    break;
                case vsCMElement.vsCMElementFunctionInvokeStmt:
                    break;
                case vsCMElement.vsCMElementPropertySetStmt:
                    break;
                case vsCMElement.vsCMElementAssignmentStmt:
                    break;
                case vsCMElement.vsCMElementInheritsStmt:
                    break;
                case vsCMElement.vsCMElementImplementsStmt:
                    break;
                case vsCMElement.vsCMElementOptionStmt:
                    break;
                case vsCMElement.vsCMElementVBAttributeStmt:
                    break;
                case vsCMElement.vsCMElementVBAttributeGroup:
                    break;
                case vsCMElement.vsCMElementEventsDeclaration:
                    break;
                case vsCMElement.vsCMElementUDTDecl:
                    break;
                case vsCMElement.vsCMElementDeclareDecl:
                    break;
                case vsCMElement.vsCMElementDefineStmt:
                    break;
                case vsCMElement.vsCMElementTypeDef:
                    break;
                case vsCMElement.vsCMElementIncludeStmt:
                    break;
                case vsCMElement.vsCMElementUsingStmt:
                    break;
                case vsCMElement.vsCMElementMacro:
                    break;
                case vsCMElement.vsCMElementMap:
                    break;
                case vsCMElement.vsCMElementIDLImport:
                    break;
                case vsCMElement.vsCMElementIDLImportLib:
                    break;
                case vsCMElement.vsCMElementIDLCoClass:
                    break;
                case vsCMElement.vsCMElementIDLLibrary:
                    break;
                case vsCMElement.vsCMElementImportStmt:
                    break;
                case vsCMElement.vsCMElementMapEntry:
                    break;
                case vsCMElement.vsCMElementVCBase:
                    break;
                case vsCMElement.vsCMElementEvent:
                    break;
                case vsCMElement.vsCMElementModule:
                    break;
                default:
                    break;
            }
            return typeStr;
        }

        #region Find References
        void _FindRefs(string refStr, string entStr, bool inverseEdge = false, int maxCount = -1)
        {
            // Logger.WriteLine("FindRef: " + refStr + " " + entStr);
            var scene = UIManager.Instance().GetScene();
            scene.AddRefs(refStr, entStr, inverseEdge, maxCount);
        }

        public void OnFindCallers(object sender, ExecutedRoutedEventArgs e)
        {
            _FindRefs("callby", "function, method");
        }
        public void OnFindCallees(object sender, ExecutedRoutedEventArgs e)
        {
            _FindRefs("call", "function, method", true);
        }
        public void OnFindMembers(object sender, ExecutedRoutedEventArgs e)
        {
            _FindRefs("declare,define,member", "dir,file,namespace", true,1);
            _FindRefs("declare,define,member", "class", true, 1);
            _FindRefs("declare,define", "variable, object", true, 1);
            _FindRefs("declare,define", "function", true, 1);
            _FindRefs("declarein,definein,memberin", "dir,file,namespace,function,class", false);
        }
        public void OnFindOverrides(object sender, ExecutedRoutedEventArgs e)
        {
            _FindRefs("overrides", "function, method", false);
            _FindRefs("overriddenby", "function, method", true);
        }
        public void OnFindBases(object sender, ExecutedRoutedEventArgs e)
        {
            _FindRefs("base", "class,struct", false);
            _FindRefs("derive", "class,struct", true);
        }
        public void OnFindUses(object sender, ExecutedRoutedEventArgs e)
        {
            _FindRefs("useby", "function, method, class, struct, file", false);
            _FindRefs("use", "variable,object,file", true);
        }
        #endregion

        #region Navigation
        public void OnGoUp(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.FindNeighbour(new Vector(0.0, -1.0), false);
        }
        public void OnGoDown(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.FindNeighbour(new Vector(0.0, 1.0), false);
        }
        public void OnGoLeft(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.FindNeighbour(new Vector(-1.0, 0.0), true);
        }
        public void OnGoRight(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.FindNeighbour(new Vector(1.0, 0.0), true);
        }
        public void OnGoUpInOrder(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.FindNeighbour(new Vector(0.0, -1.0), true);
        }
        public void OnGoDownInOrder(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.FindNeighbour(new Vector(0.0, 1.0), true);
        }
        #endregion

        #region Delete
        public void OnDelectSelectedItems(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.DeleteSelectedItems(false);
        }
        public void OnDeleteSelectedItemsAndAddToStop(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            if (scene != null)
            {
                scene.DeleteSelectedItems(true);
                var mainUI = UIManager.Instance().GetMainUI();
                mainUI.symbolWindow.UpdateForbiddenSymbol();
            }
        }
        public void OnDeleteNearbyItems(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            if (scene != null)
            {
                scene.DeleteNearbyItems();
            }
        }
        #endregion

        #region Add Symbol
        public void OnAddSimilarCodeItem(object sender, ExecutedRoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.AddSimilarCodeItem();
        }
        #endregion

        #region Scheme
        void ToggleSelectedEdgeToScheme(int ithScheme)
        {
            var scene = UIManager.Instance().GetScene();
            scene.ToggleSelectedEdgeToScheme(ithScheme);
        }

        public void OnToggleScheme1(object sender, ExecutedRoutedEventArgs e)
        {
            ToggleSelectedEdgeToScheme(0);
        }

        public void OnToggleScheme2(object sender, ExecutedRoutedEventArgs e)
        {
            ToggleSelectedEdgeToScheme(1);
        }

        public void OnToggleScheme3(object sender, ExecutedRoutedEventArgs e)
        {
            ToggleSelectedEdgeToScheme(2);
        }

        public void OnToggleScheme4(object sender, ExecutedRoutedEventArgs e)
        {
            ToggleSelectedEdgeToScheme(3);
        }

        public void OnToggleScheme5(object sender, ExecutedRoutedEventArgs e)
        {
            ToggleSelectedEdgeToScheme(4);
        }

        public void ShowScheme(int ithScheme, bool isSelected = false)
        {
            var scene = UIManager.Instance().GetScene();
            scene.ShowIthScheme(ithScheme, isSelected);
        }

        public void OnShowScheme1(object sender, ExecutedRoutedEventArgs e)
        {
            ShowScheme(0);
        }

        public void OnShowScheme2(object sender, ExecutedRoutedEventArgs e)
        {
            ShowScheme(1);
        }

        public void OnShowScheme3(object sender, ExecutedRoutedEventArgs e)
        {
            ShowScheme(2);
        }

        public void OnShowScheme4(object sender, ExecutedRoutedEventArgs e)
        {
            ShowScheme(3);
        }

        public void OnShowScheme5(object sender, ExecutedRoutedEventArgs e)
        {
            ShowScheme(4);
        }
        #endregion

        #region Custom Edge
        public void OnBeginCustomEdge(object sender, RoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.BeginAddCustomEdge();
        }

        public void OnEndCustomEdge(object sender, RoutedEventArgs e)
        {
            var scene = UIManager.Instance().GetScene();
            scene.EndAddCustomEdge();
        }
        #endregion

        public SymbolWindow GetSymbolWindow()
        {
            return this.symbolWindow;
        }

        bool _AnalyseSolution(bool useClang, bool onlySelectedProjects = false)
        {
            if (!GetCommandActive())
            {
                return false;
            }
            try
            {
                SetCommandActive(false);
                var traverser = new ProjectFileCollector();
                if (onlySelectedProjects)
                {
                    traverser.SetToSelectedProjects();
                }
                var scene = UIManager.Instance().GetScene();
                traverser.SetCustomExtension(scene.GetCustomExtensionDict());
                traverser.Traverse();
                var dirList = traverser.GetDirectoryList();
                var solutionFolder = traverser.GetSolutionFolder();

                if (dirList.Count == 0 || solutionFolder == "")
                {
                    SetCommandActive(true);
                    return false;
                }
                string doxyFolder = solutionFolder + "/CodeGraphData";
                CheckOrCreateFolder(doxyFolder);

                // Use selected projects as postfix
                string postFix = "";
                if (onlySelectedProjects)
                {
                    var projectNameList = traverser.GetSelectedProjectName();
                    foreach (string item in projectNameList)
                    {
                        postFix += "_" + item;
                    }
                }
                else
                {
                    postFix = "_solution";
                }
                postFix = postFix.Replace(" ", "");

                DoxygenDB.DoxygenDBConfig config = new DoxygenDB.DoxygenDBConfig();
                config.m_configPath = doxyFolder + "/Result" + postFix + ".graph";
                config.m_inputFolders = dirList;
                config.m_outputDirectory = doxyFolder + "/Result" + postFix;
                config.m_projectName = traverser.GetSolutionName() + postFix;
                config.m_includePaths = traverser.GetAllIncludePath();
                config.m_defines = traverser.GetAllDefines();
                config.m_useClang = useClang;
                config.m_mainLanguage = traverser.GetMainLanguage();
                config.m_customExt = scene.GetCustomExtensionDict();
                DBManager.Instance().CloseDB();

                System.Threading.Thread analysisThread = new System.Threading.Thread((ThreadStart)delegate
                {
                    try
                    {
                        if(DoxygenDB.DoxygenDB.GenerateDB(config))
                        {
                            DBManager.Instance().OpenDB(config.m_configPath);
                        }
                        else
                        {
                            SetCommandActive(true);
                        }
                    }
                    catch (Exception)
                    {
                        SetCommandActive(true);
                    }
                });
                analysisThread.Name = "Analysis Thread";
                analysisThread.Start();
            }
            catch (Exception)
            {
                Logger.Warning("Analyse failed. Please try again.");
                DBManager.Instance().CloseDB();
                SetCommandActive(true);
            }
            return true;
        }

        void AnalyseSolutionButton_Click(object sender, RoutedEventArgs e)
        {
            _AnalyseSolution(true);
        }

        public void OnFastAnalyseSolutionButton(object sender, RoutedEventArgs e)
        {
            _AnalyseSolution(false);
        }

        public void OnFastAnalyseProjectsButton(object sender, RoutedEventArgs e)
        {
            _AnalyseSolution(false, true);
        }

        private void dynamicNavigationButton_Click(object sender, RoutedEventArgs e)
        {
        }

        private void staticNavigationButton_Click(object sender, RoutedEventArgs e)
        {
        }

        public bool IsDynamicNavigation()
        {
            return dynamicNavigationButton.IsChecked == true;
        }

        private void layoutButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVertical = layoutGrid.RowDefinitions.Count != 0;
            if (isVertical)
            {
                layoutGrid.RowDefinitions.Clear();
                layoutGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(70, GridUnitType.Star) });
                layoutGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(5.0) });
                layoutGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(30, GridUnitType.Star) });
                splitter.Width = 5;
                splitter.Height = System.Double.NaN;
                Grid.SetRow(splitter, 0);
                Grid.SetColumn(splitter, 1);

                Grid.SetRow(tabControl, 0);
                Grid.SetColumn(tabControl, 2);
            }
            else
            {
                layoutGrid.ColumnDefinitions.Clear();
                layoutGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(70, GridUnitType.Star) });
                layoutGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(5.0) });
                layoutGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(30, GridUnitType.Star) });
                splitter.Height = 5;
                splitter.Width = System.Double.NaN;
                Grid.SetRow(splitter, 1);
                Grid.SetColumn(splitter, 0);

                Grid.SetRow(tabControl, 2);
                Grid.SetColumn(tabControl, 0);
            }
        }

        private void autoFocusButton_Click(object sender, RoutedEventArgs e)
        {
            codeView.SetAutoFocus(autoFocusButton.IsChecked);
        }
    }
}
