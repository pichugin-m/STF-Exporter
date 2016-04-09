#region Namespaces
using System;
using System.Text;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Lighting;
using System.Windows.Forms;
#endregion


namespace STFExporter
{
    /// <summary>
    /// Default decimal writer
    /// </summary>
    public static class DoubleExtensions
    {
        public static string ToDecimalString(this double value)
        {
            return value.ToString(CultureInfo.GetCultureInfo("en-US"));
        }
    }

    /// <summary>
    /// About dialog
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class AboutDlg : IExternalCommand
    {
        public Result Execute(Autodesk.Revit.UI.ExternalCommandData cmdData, ref string msg, ElementSet elems)
        {         
            TaskDialog helloDlg = new TaskDialog("About");

            string strMessage = "";
            strMessage += ((AssemblyProductAttribute)Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyProductAttribute), false)).Product;
            strMessage += "\n";           
            strMessage += "\n"+"Original library STF Exporter v.1.2";
            strMessage += "\n" + "Copyright 2014 � Kyle T. Morin (kyle@kylemorin.co)";
            strMessage += "\n" + "Source url www.github.com/kmorin/STF-Exporter";  
            strMessage += "\n";
            strMessage += "\n" + "Current revision library STF Exporter v.1.2.2";
            strMessage += "\n" + "File version " + Assembly.GetExecutingAssembly().GetName().Version;
            strMessage += "\n" + "Copyright 2015 � Pichugin M. (pichugin_m@mail.ru)";
            strMessage += "\n" + "This extension include support cyrillic charsets and Revit Spaces GUIDs";
            strMessage += "\n";
            
            helloDlg.MainContent = strMessage;
            helloDlg.Show();
            return Result.Cancelled;
        }
    }

    /// <summary>
    /// CmdExportAsUTF8 Class
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdExportAsUTF8 : IExternalCommand
    {
        public Application _app;
        public Document _doc;
        public string writer = "";
        public double meterMultiplier = 0.3048;
        public List<ElementId> distinctLuminaires = new List<ElementId>();
        public string stfVersionNum = "1.0.5";
        public bool intlVersion;

        public Result Execute(
          ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            _app = app;
            _doc = doc;

        // Set project units to Meters then back after
        // This is how DIALux reads the data from the STF File.

        Units pUnit = doc.GetUnits();
            FormatOptions formatOptions = pUnit.GetFormatOptions(UnitType.UT_Length);

            //DisplayUnitType curUnitType = pUnit.GetDisplayUnitType();
            DisplayUnitType curUnitType = formatOptions.DisplayUnits;

            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("STF EXPORT");
                const DisplayUnitType meters = DisplayUnitType.DUT_METERS;
                formatOptions.DisplayUnits = meters;
                // Comment out, different in 2014
                //formatOptions.Units = meters;
                //formatOptions.Rounding = 0.0000000001;]

                formatOptions.Accuracy = 0.0000000001;
                // Fix decimal symbol for int'l versions (set back again after finish)
                if (pUnit.DecimalSymbol == DecimalSymbol.Comma)
                {
                    intlVersion = true;
                    //TESTING
                    //TaskDialog.Show("INTL", "You have an internationalized version of Revit!");
                    formatOptions.UseDigitGrouping = false;
                    pUnit.DecimalSymbol = DecimalSymbol.Dot;
                }

                // Filter for only active view.
                try
                {
                    ElementLevelFilter filter = new ElementLevelFilter(doc.ActiveView.GenLevel.Id);

                    FilteredElementCollector fec = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WherePasses(filter);

                    if (fec.Count() < 1)
                    {
                        throw new NullReferenceException();
                    }

                    int numOfRooms = fec.Count();
                    writer += "[VERSION]\n"
                              + "STFF=" + stfVersionNum + "\n"
                              + "Progname=Revit\n"
                              + "Progvers=" + app.VersionNumber + "\n"
                              + "[Project]\n"
                              + "Name=" + _doc.ProjectInformation.Name + "\n"
                              + "Date=" + DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString() + "-" +
                              DateTime.Now.Day.ToString() + "\n"
                              + "Operator=" + app.Username + "\n"
                              + "NrRooms=" + numOfRooms + "\n";

                    for (int i = 1; i < numOfRooms + 1; i++)
                    {
                        string _dialuxRoomName = "Room" + i.ToString() + "=ROOM.R" + i.ToString();
                        writer += _dialuxRoomName + "\n";
                    }

                    int increment = 1;

                    // Space writer                
                    try
                    {
                        foreach (Element e in fec)
                        {
                            Space s = e as Space;
                            string roomRNum = "ROOM.R" + increment.ToString();
                            writer += "[" + roomRNum + "]\n";
                            SpaceInfoWriter(s.Id, roomRNum);
                            increment++;
                        }


                        // Write out Luminaires to bottom
                        writeLumenairs();

                        // Reset back to original units
                        formatOptions.DisplayUnits = curUnitType;
                        if (intlVersion)
                            pUnit.DecimalSymbol = DecimalSymbol.Comma;


                        tx.Commit();

                        SaveFileDialog dialog = new SaveFileDialog
                        {
                            FileName = doc.ProjectInformation.Name,
                            Filter = "File format STF | *.stf",
                            FilterIndex = 2,
                            RestoreDirectory = true
                        };

                        if (dialog.ShowDialog() == DialogResult.OK)
                        {                            
                            StreamWriter sw = new StreamWriter(dialog.FileName, false, Encoding.UTF8);
                            string[] ar = writer.Split('\n');
                            for (int i = 0; i < ar.Length; i++)
                            {   
                                sw.WriteLine(ar[i]);
                            }
                            sw.Close();                            

                            TaskDialog.Show("Done", "STF file writed.");
                        }

                        return Result.Succeeded;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        return Result.Failed;
                    }
                }
                catch (NullReferenceException)
                {
                    TaskDialog.Show("Incorrect View","Cannot find Spaces to export.\nMake sure you are in a Floorplan or Ceiling Plan view and Spaces are visible.");
                    return Result.Failed;
                }
            }
        }
        #region Private Methods
        private void writeLumenairs()
        {
            FilteredElementCollector fecFixtures = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_LightingFixtures)
            .OfClass(typeof(FamilySymbol));

            foreach (Element e in fecFixtures)
            {
                FamilySymbol fs = e as FamilySymbol;
                string load = "";
                string flux = "";

                Parameter pload = fs.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                Parameter pflux = fs.get_Parameter(BuiltInParameter.FBX_LIGHT_LIMUNOUS_FLUX);
                if (pflux != null)
                {
                    load = pload.AsValueString();
                    flux = pflux.AsValueString();

                    writer += "[" + fs.Name.Replace(" ", "") + "]\n";
                    writer += "Manufacturer=" + "\n"
                        + "Name=" + "\n"
                        + "OrderNr=" + "\n"
                        + "Box=1 1 0" + "\n" //need to fix per bounding box size (i guess);
                        + "Shape=0" + "\n"
                        + "Load=" + load.Remove(load.Length - 3) + "\n"
                        + "Flux=" + flux.Remove(flux.Length - 3) + "\n"
                        + "NrLamps=" + getNumLamps(fs) + "\n"
                        + "MountingType=1\n";
                }
            }



        }

        private string getNumLamps(FamilySymbol fs)
        {
            if (fs.LookupParameter("Number of Lamps") != null)
            {
                return fs.LookupParameter("Number of Lamps").ToString();
            }
            else
            {
                // TODO:
                // Parse from IES file
                var file = fs.get_Parameter(BuiltInParameter.FBX_LIGHT_PHOTOMETRIC_FILE);
                return "1"; //for now
            }
        }

        private void SpaceInfoWriter(ElementId spaceID, string RoomRNum)
        {
            try
            {
                //const double MAX_ROUNDING_PRECISION = 0.000000000001;

                // Get info from Space
                Space roomSpace = _doc.GetElement(spaceID) as Space;
                //Space roomSpace = _doc.get_Element(spaceID) as Space;

                // VARS
                string name = roomSpace.Name;
                string roomGUID = "{"+ roomSpace.UniqueId + "}";
                double height = roomSpace.UnboundedHeight * meterMultiplier;
                double workPlane = roomSpace.LightingCalculationWorkplane * meterMultiplier;

                // Get room vertices
                List<String> verticies = new List<string>();
                verticies = getVertexPoints(roomSpace);

                int numPoints = getVertexPointNums(roomSpace);

                // Write out Top part of room entry
                writer += "Name=" + name + "\n"
                    + "GUID=" + roomGUID + "\n"
                    + "Height=" + height.ToDecimalString() + "\n"
                    + "WorkingPlane=" + workPlane.ToDecimalString() + "\n"
                    + "NrPoints=" + numPoints.ToString() + "\n";

                // Write vertices for each point in vertex numbers
                for (int i = 0; i < numPoints; i++)
                {
                    int i2 = i + 1;
                    writer += "Point" + i2 + "=" + verticies.ElementAt(i) + "\n";
                }

                double cReflect = roomSpace.CeilingReflectance;
                double fReflect = roomSpace.FloorReflectance;
                double wReflect = roomSpace.WallReflectance;

                // Write out ceiling reflectance
                writer += "R_Ceiling=" + cReflect.ToDecimalString() + "\n";

                IList<ElementId> elemIds = roomSpace.GetMonitoredLocalElementIds();
                foreach (ElementId e in elemIds)
                {
                    TaskDialog.Show("s", _doc.GetElement(e).Name);
                }

                // Get fixtures within space
                FilteredElementCollector fec = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .OfClass(typeof(FamilyInstance));

                int count = 0;
                foreach (Element e in fec)
                {
                    FamilyInstance fi = e as FamilyInstance;
                    if (fi.Space != null)
                    {
                        if (fi.Space.Id == spaceID)
                        {
                            FamilySymbol fs = _doc.GetElement(fi.GetTypeId()) as FamilySymbol;
                            //FamilySymbol fs = _doc.get_Element(fi.GetTypeId()) as FamilySymbol;

                            int lumNum = count + 1;
                            string lumName = "Lum" + lumNum.ToString();
                            LocationPoint locpt = fi.Location as LocationPoint;
                            XYZ fixtureloc = locpt.Point;
                            double X = fixtureloc.X * meterMultiplier;
                            double Y = fixtureloc.Y * meterMultiplier;
                            double Z = fixtureloc.Z * meterMultiplier;

                            string fsGUID = "{" + fs.UniqueId + "}";

                            double rotation = locpt.Rotation;
                            writer += lumName + "=" + fs.Name.Replace(" ", "") + "\n";
                            writer += lumName + ".GUID=" + fsGUID + "\n";
                            writer += lumName + ".Pos=" + X.ToDecimalString() + " " + Y.ToDecimalString() + " " + Z.ToDecimalString() + "\n";
                            writer += lumName + ".Rot=0 0 0" + "\n"; //need to figure out this rotation; Update: cannot determine. Almost impossible for Dialux

                            count++;
                        }
                    }
                }

                // Write out Lums part
                writer += "NrLums=" + count.ToString() + "\n";
                // Write out Struct part
                writer += "NrStruct=0\n";
                // Write out Furn part
                writer += "NrFurns=" + getFurns(spaceID, RoomRNum);

            }
            catch (IndexOutOfRangeException)
            {
                throw new IndexOutOfRangeException();
            }
        }

        private string getFurns(ElementId spaceID, string RoomRNum)
        {
            string furnsOutput = String.Empty;
            // Go through the room and write out the windows/doors

            // DOORS //
            // Get all doors that have space where id equals current space.
            FilteredElementCollector fecDoors = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Doors)
            .OfClass(typeof(FamilyInstance));

            // WINDOWS //
            FilteredElementCollector fecWindows = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Windows)
            .OfClass(typeof(FamilyInstance));

            List<Element> doorsList = new List<Element>();
            List<Element> windowList = new List<Element>();

            foreach (Element e in fecDoors)
            {
                FamilyInstance fi = e as FamilyInstance;
                if (fi != null && fi.Space != null && fi.Space.Id == spaceID)
                    doorsList.Add(fi);
            }

            foreach (Element e in fecWindows)
            {
                FamilyInstance fi = e as FamilyInstance;
                if (fi != null && fi.Space != null && fi.Space.Id == spaceID)
                    windowList.Add(fi);
            }

            //Add Number of Furns to string
            furnsOutput += (doorsList.Count + windowList.Count).ToString() + "\n";

            // Loop through new list of Doors
            int furnNumber = 1;
            foreach (Element e in doorsList)
            {
                FamilyInstance fi = e as FamilyInstance;
                // Door Width (in meters)
                string doorWidth = (fi.Symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH).AsDouble() * 0.3048).ToDecimalString();
                // Door height (in meters)
                string doorHeight = (fi.Symbol.get_Parameter(BuiltInParameter.DOOR_HEIGHT).AsDouble() * 0.3048).ToDecimalString();
                LocationPoint lp = fi.Location as LocationPoint;
                XYZ p = new XYZ(lp.Point.X * 0.30, lp.Point.Y * 0.30, 0);
                //XYZ p = new XYZ(lp.Point.X, lp.Point.Y, 0);
                string lps = p.ToString().Substring(1, p.ToString().Length - 2);
                //string lps = lp.Point.ToDecimalString().Substring(1, lp.Point.ToDecimalString().Length - 2);

                string sfurnNumber = "Furn" + furnNumber.ToString();
                //Furn1=door
                //Furn1.Ref=ROOM.R1.F1
                //Furn1.Rot=90.00 0.00 0.00
                //Furn1.Pos=1.151 3.67 0
                //Furn1.Size=1.0 2.0 0.0
                furnsOutput += sfurnNumber + "=door\n";
                furnsOutput += sfurnNumber + ".Ref=" + RoomRNum + ".F" + furnNumber.ToString() + "\n";
                // TODO: fix rotation
                furnsOutput += sfurnNumber + ".Rot=90.00 0.00 0.00" + "\n"; //rotation???
                // TODO: fix positioning...
                furnsOutput += sfurnNumber + ".Pos=" + lps + "\n";
                furnsOutput += sfurnNumber + ".Size=" + doorWidth + " " + doorHeight + " 0.00\n";

                //Inrement furns
                furnNumber++;
            }

            // WINDOWS //

            foreach (Element e in fecWindows)
            {
                FamilyInstance fi = e as FamilyInstance;
                // Window Width
                string windowWidth = (fi.Symbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH).AsDouble() * 0.3048).ToDecimalString();
                // Window Height
                string windowHeight = (fi.Symbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT).AsDouble() * 0.3048).ToDecimalString();
                LocationPoint lp = fi.Location as LocationPoint;
                //XYZ p = new XYZ(lp.Point.X * 0.30, lp.Point.Y * 0.30, lp.Point.Z * 0.30);
                Transform t1 = fi.GetTransform();
                XYZ p = new XYZ(t1.BasisX.X * 0.30,t1.BasisX.Y*0.30,lp.Point.Z * 0.30);
                string lps = p.ToString().Substring(1, p.ToString().Length - 2);

                string sFurnNumber = "Furn" + furnNumber.ToString();
                furnsOutput += sFurnNumber + "=win\n";
                furnsOutput += sFurnNumber + ".Ref=" + RoomRNum + ".F" + furnNumber.ToString() + "\n";
                // TODO: fix rotation
                furnsOutput += sFurnNumber + ".Rot=90.00 0.00 0.00" + "\n"; //rotation???
                // TODO: fix positioning...
                furnsOutput += sFurnNumber + ".Pos=" + lps + "\n";
                furnsOutput += sFurnNumber + ".Size=" + windowWidth + " " + windowHeight + " 0.00\n";

                //Inrement furns
                furnNumber++;

            }


            return furnsOutput;
        }

        private List<string> getVertexPoints(Space roomSpace)
        {
            List<string> verticies = new List<string>();
            SpatialElementBoundaryOptions opts = new SpatialElementBoundaryOptions();
            IList<IList<Autodesk.Revit.DB.BoundarySegment>> bsa = roomSpace.GetBoundarySegments(opts);
            if (bsa.Count > 0)
            {
                foreach (Autodesk.Revit.DB.BoundarySegment bs in bsa[0])
                {
                    // For 2014
                    //var X = bs.Curve.get_EndPoint(0).X * meterMultiplier;
                    //var Y = bs.Curve.get_EndPoint(0).Y * meterMultiplier;
                    var X = bs.Curve.GetEndPoint(0).X * meterMultiplier;
                    var Y = bs.Curve.GetEndPoint(0).Y * meterMultiplier;

                    verticies.Add(X.ToDecimalString() + " " + Y.ToDecimalString());
                }

            }
            else
            {
                verticies.Add("0 0");
            }
            return verticies;
        }

        private int getVertexPointNums(Space roomSpace)
        {
            SpatialElementBoundaryOptions opts = new SpatialElementBoundaryOptions();
            try
            {
                IList<IList<Autodesk.Revit.DB.BoundarySegment>> bsa = roomSpace.GetBoundarySegments(opts);

                return bsa[0].Count;
            }
            catch (Exception)
            {
                TaskDialog.Show("OOPS!", "Seems you have a Space in your view that is not in a properly enclosed region. \n\nPlease remove these Spaces or re-establish them inside of boundary walls and run the Exporter again.");
                throw new IndexOutOfRangeException();
            }

        }
        #endregion
    }

    /// <summary>
    /// CmdExportAsCP1251 Class
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdExportAsCP1251 : IExternalCommand
    {
        public Application _app;
        public Document _doc;
        public string writer = "";
        public double meterMultiplier = 0.3048;
        public List<ElementId> distinctLuminaires = new List<ElementId>();
        public string stfVersionNum = "1.0.5";
        public bool intlVersion;

        public Result Execute(
          ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            _app = app;
            _doc = doc;

            // Set project units to Meters then back after
            // This is how DIALux reads the data from the STF File.

            Units pUnit = doc.GetUnits();
            FormatOptions formatOptions = pUnit.GetFormatOptions(UnitType.UT_Length);

            //DisplayUnitType curUnitType = pUnit.GetDisplayUnitType();
            DisplayUnitType curUnitType = formatOptions.DisplayUnits;

            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("STF EXPORT");
                const DisplayUnitType meters = DisplayUnitType.DUT_METERS;
                formatOptions.DisplayUnits = meters;
                // Comment out, different in 2014
                //formatOptions.Units = meters;
                //formatOptions.Rounding = 0.0000000001;]

                formatOptions.Accuracy = 0.0000000001;
                // Fix decimal symbol for int'l versions (set back again after finish)
                if (pUnit.DecimalSymbol == DecimalSymbol.Comma)
                {
                    intlVersion = true;
                    //TESTING
                    //TaskDialog.Show("INTL", "You have an internationalized version of Revit!");
                    formatOptions.UseDigitGrouping = false;
                    pUnit.DecimalSymbol = DecimalSymbol.Dot;
                }

                // Filter for only active view.
                try
                {
                    ElementLevelFilter filter = new ElementLevelFilter(doc.ActiveView.GenLevel.Id);

                    FilteredElementCollector fec = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WherePasses(filter);

                    if (fec.Count() < 1)
                    {
                        throw new NullReferenceException();
                    }

                    int numOfRooms = fec.Count();
                    writer += "[VERSION]\n"
                              + "STFF=" + stfVersionNum + "\n"
                              + "Progname=Revit\n"
                              + "Progvers=" + app.VersionNumber + "\n"
                              + "[Project]\n"
                              + "Name=" + _doc.ProjectInformation.Name + "\n"
                              + "Date=" + DateTime.Now.Year.ToString() + "-" + DateTime.Now.Month.ToString() + "-" +
                              DateTime.Now.Day.ToString() + "\n"
                              + "Operator=" + app.Username + "\n"
                              + "NrRooms=" + numOfRooms + "\n";

                    for (int i = 1; i < numOfRooms + 1; i++)
                    {
                        string _dialuxRoomName = "Room" + i.ToString() + "=ROOM.R" + i.ToString();
                        writer += _dialuxRoomName + "\n";
                    }

                    int increment = 1;

                    // Space writer                
                    try
                    {
                        foreach (Element e in fec)
                        {
                            Space s = e as Space;
                            string roomRNum = "ROOM.R" + increment.ToString();
                            writer += "[" + roomRNum + "]\n";
                            SpaceInfoWriter(s.Id, roomRNum);
                            increment++;
                        }


                        // Write out Luminaires to bottom
                        writeLumenairs();

                        // Reset back to original units
                        formatOptions.DisplayUnits = curUnitType;
                        if (intlVersion)
                            pUnit.DecimalSymbol = DecimalSymbol.Comma;


                        tx.Commit();

                        SaveFileDialog dialog = new SaveFileDialog
                        {
                            FileName = doc.ProjectInformation.Name,
                            Filter = "File format STF | *.stf",
                            FilterIndex = 2,
                            RestoreDirectory = true
                        };

                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            //fix - file created with CP1251 charset for not english Revit versions.
                            StreamWriter sw = new StreamWriter(dialog.FileName, false, Encoding.GetEncoding("windows-1251"));
                            string[] ar = writer.Split('\n');
                            for (int i = 0; i < ar.Length; i++)
                            {
                                sw.WriteLine(ar[i]);
                            }
                            sw.Close();

                            TaskDialog.Show("Done", "STF file writed.");
                        }

                        return Result.Succeeded;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        return Result.Failed;
                    }
                }
                catch (NullReferenceException)
                {
                    TaskDialog.Show("Incorrect View", "Cannot find Spaces to export.\nMake sure you are in a Floorplan or Ceiling Plan view and Spaces are visible.");
                    return Result.Failed;
                }
            }
        }
        #region Private Methods
        private void writeLumenairs()
        {
            FilteredElementCollector fecFixtures = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_LightingFixtures)
            .OfClass(typeof(FamilySymbol));

            foreach (Element e in fecFixtures)
            {
                FamilySymbol fs = e as FamilySymbol;
                string load = "";
                string flux = "";

                Parameter pload = fs.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                Parameter pflux = fs.get_Parameter(BuiltInParameter.FBX_LIGHT_LIMUNOUS_FLUX);
                if (pflux != null)
                {
                    load = pload.AsValueString();
                    flux = pflux.AsValueString();

                    writer += "[" + fs.Name.Replace(" ", "") + "]\n";
                    writer += "Manufacturer=" + "\n"
                        + "Name=" + "\n"
                        + "OrderNr=" + "\n"
                        + "Box=1 1 0" + "\n" //need to fix per bounding box size (i guess);
                        + "Shape=0" + "\n"
                        + "Load=" + load.Remove(load.Length - 3) + "\n"
                        + "Flux=" + flux.Remove(flux.Length - 3) + "\n"
                        + "NrLamps=" + getNumLamps(fs) + "\n"
                        + "MountingType=1\n";
                }
            }



        }

        private string getNumLamps(FamilySymbol fs)
        {
            if (fs.LookupParameter("Number of Lamps") != null)
            {
                return fs.LookupParameter("Number of Lamps").ToString();
            }
            else
            {
                // TODO:
                // Parse from IES file
                var file = fs.get_Parameter(BuiltInParameter.FBX_LIGHT_PHOTOMETRIC_FILE);
                return "1"; //for now
            }
        }

        private void SpaceInfoWriter(ElementId spaceID, string RoomRNum)
        {
            try
            {
                //const double MAX_ROUNDING_PRECISION = 0.000000000001;

                // Get info from Space
                Space roomSpace = _doc.GetElement(spaceID) as Space;
                //Space roomSpace = _doc.get_Element(spaceID) as Space;

                // VARS
                string name = roomSpace.Name;
                string roomGUID = "{" + roomSpace.UniqueId + "}";
                double height = roomSpace.UnboundedHeight * meterMultiplier;
                double workPlane = roomSpace.LightingCalculationWorkplane * meterMultiplier;

                // Get room vertices
                List<String> verticies = new List<string>();
                verticies = getVertexPoints(roomSpace);

                int numPoints = getVertexPointNums(roomSpace);

                // Write out Top part of room entry
                writer += "Name=" + name + "\n"
                    + "GUID=" + roomGUID + "\n"
                    + "Height=" + height.ToDecimalString() + "\n"
                    + "WorkingPlane=" + workPlane.ToDecimalString() + "\n"
                    + "NrPoints=" + numPoints.ToString() + "\n";

                // Write vertices for each point in vertex numbers
                for (int i = 0; i < numPoints; i++)
                {
                    int i2 = i + 1;
                    writer += "Point" + i2 + "=" + verticies.ElementAt(i) + "\n";
                }

                double cReflect = roomSpace.CeilingReflectance;
                double fReflect = roomSpace.FloorReflectance;
                double wReflect = roomSpace.WallReflectance;

                // Write out ceiling reflectance
                writer += "R_Ceiling=" + cReflect.ToDecimalString() + "\n";

                IList<ElementId> elemIds = roomSpace.GetMonitoredLocalElementIds();
                foreach (ElementId e in elemIds)
                {
                    TaskDialog.Show("s", _doc.GetElement(e).Name);
                }

                // Get fixtures within space
                FilteredElementCollector fec = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .OfClass(typeof(FamilyInstance));

                int count = 0;
                foreach (Element e in fec)
                {
                    FamilyInstance fi = e as FamilyInstance;
                    if (fi.Space != null)
                    {
                        if (fi.Space.Id == spaceID)
                        {
                            FamilySymbol fs = _doc.GetElement(fi.GetTypeId()) as FamilySymbol;
                            //FamilySymbol fs = _doc.get_Element(fi.GetTypeId()) as FamilySymbol;

                            int lumNum = count + 1;
                            string lumName = "Lum" + lumNum.ToString();
                            LocationPoint locpt = fi.Location as LocationPoint;
                            XYZ fixtureloc = locpt.Point;
                            double X = fixtureloc.X * meterMultiplier;
                            double Y = fixtureloc.Y * meterMultiplier;
                            double Z = fixtureloc.Z * meterMultiplier;

                            string fsGUID = "{" + fs.UniqueId + "}";

                            double rotation = locpt.Rotation;
                            writer += lumName + "=" + fs.Name.Replace(" ", "") + "\n";
                            writer += lumName + ".GUID=" + fsGUID + "\n";
                            writer += lumName + ".Pos=" + X.ToDecimalString() + " " + Y.ToDecimalString() + " " + Z.ToDecimalString() + "\n";
                            writer += lumName + ".Rot=0 0 0" + "\n"; //need to figure out this rotation; Update: cannot determine. Almost impossible for Dialux

                            count++;
                        }
                    }
                }

                // Write out Lums part
                writer += "NrLums=" + count.ToString() + "\n";
                // Write out Struct part
                writer += "NrStruct=0\n";
                // Write out Furn part
                writer += "NrFurns=" + getFurns(spaceID, RoomRNum);

            }
            catch (IndexOutOfRangeException)
            {
                throw new IndexOutOfRangeException();
            }
        }

        private string getFurns(ElementId spaceID, string RoomRNum)
        {
            string furnsOutput = String.Empty;
            // Go through the room and write out the windows/doors

            // DOORS //
            // Get all doors that have space where id equals current space.
            FilteredElementCollector fecDoors = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Doors)
            .OfClass(typeof(FamilyInstance));

            // WINDOWS //
            FilteredElementCollector fecWindows = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Windows)
            .OfClass(typeof(FamilyInstance));

            List<Element> doorsList = new List<Element>();
            List<Element> windowList = new List<Element>();

            foreach (Element e in fecDoors)
            {
                FamilyInstance fi = e as FamilyInstance;
                if (fi != null && fi.Space != null && fi.Space.Id == spaceID)
                    doorsList.Add(fi);
            }

            foreach (Element e in fecWindows)
            {
                FamilyInstance fi = e as FamilyInstance;
                if (fi != null && fi.Space != null && fi.Space.Id == spaceID)
                    windowList.Add(fi);
            }

            //Add Number of Furns to string
            furnsOutput += (doorsList.Count + windowList.Count).ToString() + "\n";

            // Loop through new list of Doors
            int furnNumber = 1;
            foreach (Element e in doorsList)
            {
                FamilyInstance fi = e as FamilyInstance;
                // Door Width (in meters)
                string doorWidth = (fi.Symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH).AsDouble() * 0.3048).ToDecimalString();
                // Door height (in meters)
                string doorHeight = (fi.Symbol.get_Parameter(BuiltInParameter.DOOR_HEIGHT).AsDouble() * 0.3048).ToDecimalString();
                LocationPoint lp = fi.Location as LocationPoint;
                XYZ p = new XYZ(lp.Point.X * 0.30, lp.Point.Y * 0.30, 0);
                //XYZ p = new XYZ(lp.Point.X, lp.Point.Y, 0);
                string lps = p.ToString().Substring(1, p.ToString().Length - 2);
                //string lps = lp.Point.ToDecimalString().Substring(1, lp.Point.ToDecimalString().Length - 2);

                string sfurnNumber = "Furn" + furnNumber.ToString();
                //Furn1=door
                //Furn1.Ref=ROOM.R1.F1
                //Furn1.Rot=90.00 0.00 0.00
                //Furn1.Pos=1.151 3.67 0
                //Furn1.Size=1.0 2.0 0.0
                furnsOutput += sfurnNumber + "=door\n";
                furnsOutput += sfurnNumber + ".Ref=" + RoomRNum + ".F" + furnNumber.ToString() + "\n";
                // TODO: fix rotation
                furnsOutput += sfurnNumber + ".Rot=90.00 0.00 0.00" + "\n"; //rotation???
                // TODO: fix positioning...
                furnsOutput += sfurnNumber + ".Pos=" + lps + "\n";
                furnsOutput += sfurnNumber + ".Size=" + doorWidth + " " + doorHeight + " 0.00\n";

                //Inrement furns
                furnNumber++;
            }

            // WINDOWS //

            foreach (Element e in fecWindows)
            {
                FamilyInstance fi = e as FamilyInstance;
                // Window Width
                string windowWidth = (fi.Symbol.get_Parameter(BuiltInParameter.WINDOW_WIDTH).AsDouble() * 0.3048).ToDecimalString();
                // Window Height
                string windowHeight = (fi.Symbol.get_Parameter(BuiltInParameter.WINDOW_HEIGHT).AsDouble() * 0.3048).ToDecimalString();
                LocationPoint lp = fi.Location as LocationPoint;
                //XYZ p = new XYZ(lp.Point.X * 0.30, lp.Point.Y * 0.30, lp.Point.Z * 0.30);
                Transform t1 = fi.GetTransform();
                XYZ p = new XYZ(t1.BasisX.X * 0.30, t1.BasisX.Y * 0.30, lp.Point.Z * 0.30);
                string lps = p.ToString().Substring(1, p.ToString().Length - 2);

                string sFurnNumber = "Furn" + furnNumber.ToString();
                furnsOutput += sFurnNumber + "=win\n";
                furnsOutput += sFurnNumber + ".Ref=" + RoomRNum + ".F" + furnNumber.ToString() + "\n";
                // TODO: fix rotation
                furnsOutput += sFurnNumber + ".Rot=90.00 0.00 0.00" + "\n"; //rotation???
                // TODO: fix positioning...
                furnsOutput += sFurnNumber + ".Pos=" + lps + "\n";
                furnsOutput += sFurnNumber + ".Size=" + windowWidth + " " + windowHeight + " 0.00\n";

                //Inrement furns
                furnNumber++;

            }


            return furnsOutput;
        }

        private List<string> getVertexPoints(Space roomSpace)
        {
            List<string> verticies = new List<string>();
            SpatialElementBoundaryOptions opts = new SpatialElementBoundaryOptions();
            IList<IList<Autodesk.Revit.DB.BoundarySegment>> bsa = roomSpace.GetBoundarySegments(opts);
            if (bsa.Count > 0)
            {
                foreach (Autodesk.Revit.DB.BoundarySegment bs in bsa[0])
                {
                    // For 2014
                    //var X = bs.Curve.get_EndPoint(0).X * meterMultiplier;
                    //var Y = bs.Curve.get_EndPoint(0).Y * meterMultiplier;
                    var X = bs.Curve.GetEndPoint(0).X * meterMultiplier;
                    var Y = bs.Curve.GetEndPoint(0).Y * meterMultiplier;

                    verticies.Add(X.ToDecimalString() + " " + Y.ToDecimalString());
                }

            }
            else
            {
                verticies.Add("0 0");
            }
            return verticies;
        }

        private int getVertexPointNums(Space roomSpace)
        {
            SpatialElementBoundaryOptions opts = new SpatialElementBoundaryOptions();
            try
            {
                IList<IList<Autodesk.Revit.DB.BoundarySegment>> bsa = roomSpace.GetBoundarySegments(opts);

                return bsa[0].Count;
            }
            catch (Exception)
            {
                TaskDialog.Show("OOPS!", "Seems you have a Space in your view that is not in a properly enclosed region. \n\nPlease remove these Spaces or re-establish them inside of boundary walls and run the Exporter again.");
                throw new IndexOutOfRangeException();
            }

        }
        #endregion
    }


}