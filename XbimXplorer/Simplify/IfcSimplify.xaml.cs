﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace XbimXplorer.Simplify
{
    /// <summary>
    /// Interaction logic for IfcSimplify.xaml
    /// </summary>
    public partial class IfcSimplify : Window
    {
        /// <summary>
        /// 
        /// </summary>
        public IfcSimplify()
        {
            InitializeComponent();
        }

        private Dictionary<int, string> _ifcLines = new Dictionary<int, string>();
        private Dictionary<int, string> _ifcContents = new Dictionary<int, string>();
        private Dictionary<int, string> _ifcType = new Dictionary<int, string>();
        private List<int> _elementsToExport = new List<int>();
        private Dictionary<string, int> _guids = new Dictionary<string, int>();

        private string _header;
        private string _footer;

        private enum SectionMode
        {
            Header,
            Data,
            Footer
        }
        
        private void cmdInit_Click(object sender, RoutedEventArgs e)
        {
            InitialiseFile();
        }

        private void InitialiseFile()
        {
            _guids = new Dictionary<string, int>();
            _ifcLines = new Dictionary<int, string>();
            _ifcContents = new Dictionary<int, string>();
            _elementsToExport = new List<int>();
            _ifcType = new Dictionary<int, string>();
            _header = "";
            _footer = "";

            var mode = SectionMode.Header;

            var fp = new FileTextParser(TxtInputFile.Text);
            string readLine;
            var requiredLines = new List<int>();
            var lineBuffer = "";

            var re = new Regex(
                "#(\\d+)" + // integer index
                " *" + // optional spaces
                "=" + // =
                " *" + // optional spaces
                "([^ (]*)" +  // class information type (anything but an open bracket as many times)
                " *" + // optional spaces
                "\\(" + // the open bracket (escaped)
                "(.*)" + // anything repeated
                "\\) *;" // the closing bracket escaped and the semicolon
                );

            var reGuid = new Regex(@"^ *'([^']*)' *,");

            while ((readLine = fp.NextLine()) != null)
            {
                if (readLine.ToLowerInvariant().Trim() == "data;")
                {
                    _header += readLine + "\r\n";
                    mode = SectionMode.Data;
                }
                else if (mode == SectionMode.Data && readLine.ToLowerInvariant().Trim() == "endsec;")
                {
                    _footer += readLine + "\r\n";
                    mode = SectionMode.Footer;
                }
                else if (mode == SectionMode.Data)
                {
                    lineBuffer += readLine;
                    var m = re.Match(lineBuffer);
                    if (!m.Success)
                        continue;
                    var iId = Convert.ToInt32(m.Groups[1].ToString());
                    var type = m.Groups[2].ToString();

                    var content = m.Groups[3].Value;
                    _ifcLines.Add(iId, lineBuffer);
                    _ifcContents.Add(iId, content);
                    _ifcType.Add(iId, type);

                    var mGuid = reGuid.Match(content);
                    if (mGuid.Success)
                    {
                        var val = mGuid.Groups[1].Value;
                        if (!_guids.ContainsKey(val))
                            _guids.Add(val, iId);
                    }

                    if (type == "IFCPROJECT")
                        requiredLines.Add(iId);
                    lineBuffer = "";
                }
                else
                {
                    if (mode == SectionMode.Header)
                        _header += readLine + "\r\n";
                    else
                        _footer += readLine + "\r\n";
                }
            }
            fp.Close();
            fp.Dispose();
            GCommands.IsEnabled = true;
            CmdSave.IsEnabled = true;

            
            foreach (var i in requiredLines)
            {
                RecursiveAdd(i);
            }
            
            UpdateExportList();
        }

        private int SelectedIfcIndex
        {
            get
            {
                var iConv = -1;
                try
                {
                    iConv = Convert.ToInt32(TxtEntityLabelAdd.Text);
                }
                catch
                {
                    if (!ChkGuid.IsChecked.Value)
                        return iConv;
                    var k = _guids.Keys.FirstOrDefault(x => x.Contains(TxtEntityLabelAdd.Text));
                    if (k != null)
                        return _guids[k];
                }
                return iConv;
            }
        }

        private void txtEntityLabelAdd_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtEntityLabelAdd.Text == "")
            {
                UpdateExportList();
                return;
            }

            var ic = SelectedIfcIndex;
            InfoBlock.Text = _ifcLines.ContainsKey(ic) 
                ? _ifcLines[ic] +
                    (
                    _elementsToExport.Contains(ic) 
                        ? " (already selected)"
                        : ""
                    )
                : "Not found";
        }

        private void RecursiveAdd(int ifcIndex)
        {
            if (_elementsToExport.Contains(ifcIndex))
                return; // been exported already;
            _elementsToExport.Add(ifcIndex);
            var re = new Regex(
                "#(\\d+)" + // hash and integer index
                ""
                );
            try
            {
                var mc = re.Matches(_ifcContents[ifcIndex]);
                foreach (Match mtch in mc)
                {
                    var thisIndex = Convert.ToInt32(mtch.Groups[1].ToString());
                    //if (!_ElementsToExport.Contains(ThisIndex))
                    //    _ElementsToExport.Add(ThisIndex);
                    RecursiveAdd(thisIndex);
                }
            }
            catch
            {
                // ignored
            }
        }

        private void CmdAdd_Click(object sender, RoutedEventArgs e)
        {
            var v = SelectedIfcIndex;
            TxtHandPicked.Text += SelectedIfcIndex + Environment.NewLine;
            RecursiveAdd(SelectedIfcIndex);

            ConsiderManualSelection();
        }

        private void UpdateExportList()
        {
            _elementsToExport.Sort();
            var sb = new StringBuilder();
            ElementCount.Text = "Element selected: " + _elementsToExport.Count;
            foreach (var i in _elementsToExport)
            {
                try
                {
                    sb.AppendLine($"{i}:{_ifcType[i]}");
                }
                catch
                {
                    // ignored
                }
            }
            TxtOutput.Text = sb.ToString();
        }

        private void cmdSave_Click(object sender, RoutedEventArgs e)
        {
            var t = new FileInfo(TxtInputFile.Text + ".stripped.ifc");
            var tex = t.CreateText();

            tex.Write(_header);
            foreach (var i in _elementsToExport)
            {
                tex.WriteLine(_ifcLines[i]);
            }
            tex.Write(_footer);
            tex.Close();
            TxtOutput.Text = "Done";
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Configure open file dialog box
            var dlg = new OpenFileDialog();
            dlg.FileName = ""; // Default file name
            dlg.DefaultExt = ".ifc"; // Default file extension
            dlg.Filter = "Ifc files (.ifc)|*.ifc"; // Filter files by extension 

            // Show open file dialog box
            var result = dlg.ShowDialog();

            // Process open file dialog box results 
            if (result != true)
                return;
            // set document 
            TxtInputFile.Text = dlg.FileName;
            if (!string.IsNullOrEmpty(TxtInputFile.Text))
                return;
            // open document 
            InitialiseFile();
        }

        private void txtCommand_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter || (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)))
                return;

            ConsiderManualSelection();

        }

        private void ConsiderManualSelection()
        {
            var re = new Regex(" *(\\d+)");
            var sb = new StringBuilder();

            var lines = TxtHandPicked.Text.Split(new [] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                
                var m = re.Match(line);
                if (!m.Success)
                {
                    sb.AppendLine(line);
                    continue;
                }

                var iLab = Convert.ToInt32(m.Groups[1].Value);

                if (!_elementsToExport.Contains(iLab))
                {
                    RecursiveAdd(iLab);
                }
                sb.AppendLine($"{iLab}: {_ifcType[iLab]}");
            }
            TxtHandPicked.Text = sb.ToString();
            TxtHandPicked.CaretIndex = TxtHandPicked.Text.Length;
            UpdateExportList();
        }
    }
}
