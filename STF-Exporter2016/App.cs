#region Namespaces
using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using System.Windows.Media.Imaging;
#endregion

namespace STFExporter
{
    class App : IExternalApplication
    {
        static readonly string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        static readonly string assyPath = Path.Combine(dir, "STFExporter2016.dll");
        static readonly string _imgFolder = Path.Combine(dir, "Images");
        
        public Result OnStartup(UIControlledApplication a)
        {
            try
            {
                AddRibbonPanel(a);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ribbon", ex.ToString());                
            }

            return Result.Succeeded;
        }

        private void AddRibbonPanel(UIControlledApplication app)
        {
            RibbonPanel rvtRibbonPanel = app.CreateRibbonPanel("STF Exporter: v" + Assembly.GetExecutingAssembly().GetName().Version);
            PulldownButtonData data = new PulldownButtonData("Options", "STF Export");
            RibbonItem pb_item = rvtRibbonPanel.AddItem(data);
            PulldownButton optionsBtn = pb_item as PulldownButton;

            optionsBtn.AddPushButton(new PushButtonData("STF Export CP1251", "Export with CP1251 charsets", assyPath, "STFExporter.CmdExportAsCP1251"));
            optionsBtn.AddPushButton(new PushButtonData("STF Export UTF8", "Export with UTF8 charsets", assyPath, "STFExporter.CmdExportAsUTF8"));
            optionsBtn.AddPushButton(new PushButtonData("About..", "About dialog", assyPath, "STFExporter.AboutDlg"));

            optionsBtn.LargeImage = NewBitmapImage("stfexport.png");
            optionsBtn.ToolTip = "Export Revit Spaces to STF file";
            optionsBtn.LongDescription = "Exports Spaces in Revit model to STF file for use in application such as DIALux";

            //ContextualHe/lp contextHelp = new ContextualHelp(ContextualHelpType.ChmFile, dir + "/Resources/STFExporter Help.htm");
            ContextualHelp contextHelp = new ContextualHelp(ContextualHelpType.Url, "https://github.com/kmorin/STF-Exporter");

            pb_item.SetContextualHelp(contextHelp);
        }

        BitmapImage NewBitmapImage(string imgName)
        {
            return new BitmapImage(new Uri(Path.Combine(_imgFolder, imgName)));
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }
}
