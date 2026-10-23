using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Models;
using Revit26_Plugin_Revised.LinkVersionChecker.V007.Core.Services;
using Xunit;

namespace LinkVersionChecker.Tests
{
    public class AccPathTests
    {
        [Theory]
        [InlineData("Autodesk Docs://ALEC/Wynn Marjan Island/ARC.rvt", true)]
        [InlineData("autodesk docs://Wynn/ARC.rvt", true)]
        [InlineData("BIM 360://Project/ARC.rvt", true)]
        [InlineData(@"C:\Users\Rafi\ACCDocs\ALEC\Wynn\Project Files\ARC.rvt", false)]
        [InlineData(@"\\server\share\ARC.rvt", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsCloudPath(string path, bool expected) => Assert.Equal(expected, AccPath.IsCloudPath(path));

        [Fact]
        public void Cloud_AccountProjectFolder()
        {
            var p = AccPath.ParseCloud("Autodesk Docs://ALEC Engineering and Contracting LLC/Wynn Marjan Island/02_ARC/Models/WMI_ARC_Tower.rvt");
            Assert.Equal("ALEC Engineering and Contracting LLC", p.Account);
            Assert.Equal("Wynn Marjan Island", p.Project);
            Assert.Equal(@"02_ARC\Models", p.Folder);
            Assert.Equal("WMI_ARC_Tower.rvt", p.FileName);
        }

        [Fact]
        public void Cloud_WithProjectFilesLevel()
        {
            var p = AccPath.ParseCloud("Autodesk Docs://ALEC/Wynn Marjan Island/Project Files/03_STR/STR.rvt");
            Assert.Equal("ALEC", p.Account);
            Assert.Equal("Wynn Marjan Island", p.Project);
            Assert.Equal("03_STR", p.Folder);
        }

        [Fact]
        public void Cloud_WithoutAccount_KnownProject()
        {
            var p = AccPath.ParseCloud("Autodesk Docs://Wynn Marjan Island/02_ARC/ARC.rvt", n => n == "Wynn Marjan Island");
            Assert.Equal("", p.Account);
            Assert.Equal("Wynn Marjan Island", p.Project);
            Assert.Equal("02_ARC", p.Folder);
        }

        [Fact]
        public void Cloud_ModelAtProjectRoot()
        {
            var p = AccPath.ParseCloud("Autodesk Docs://Wynn Marjan Island/ARC.rvt");
            Assert.Equal("Wynn Marjan Island", p.Project);
            Assert.Equal("(project root)", p.Folder);
            Assert.Equal("ARC.rvt", p.FileName);
        }

        [Fact]
        public void DesktopConnector_AccountAndProject()
        {
            var p = AccPath.ParseDesktopConnector(@"C:\Users\Rafi\ACCDocs\ALEC Engineering and Contracting LLC\Wynn Marjan Island\Project Files\02_ARC\ARC.rvt");
            Assert.Equal("ALEC Engineering and Contracting LLC", p.Account);
            Assert.Equal("Wynn Marjan Island", p.Project);
            Assert.Equal("ARC.rvt", p.FileName);
        }

        [Fact]
        public void DesktopConnector_NotAccDocs() =>
            Assert.Equal("", AccPath.ParseDesktopConnector(@"D:\Projects\Wynn\ARC.rvt").Project);

        [Theory]
        [InlineData("b.7f3c0000-0000-0000-0000-00000000e21a", "7F3C0000-0000-0000-0000-00000000E21A", true)]
        [InlineData("{7f3c0000-0000-0000-0000-00000000e21a}", "7f3c0000-0000-0000-0000-00000000e21a", true)]
        [InlineData("7f3c0000-0000-0000-0000-00000000e21a", "11110000-0000-0000-0000-00000000e21a", false)]
        [InlineData("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000", false)]
        [InlineData("", "", false)]
        public void SameId(string a, string b, bool expected) => Assert.Equal(expected, AccPath.SameId(a, b));

        [Fact]
        public void CloudRelativePath_MirrorsDesktopConnectorTree() =>
            Assert.Equal(@"ALEC Engineering and Contracting LLC\Wynn Marjan Island\Project Files\02 - SHARED\ALEC - DESIGN\ARC L23-RF.rvt",
                AccPath.CloudRelativePath("Autodesk Docs://ALEC Engineering and Contracting LLC/Wynn Marjan Island/Project Files/02 - SHARED/ALEC - DESIGN/ARC L23-RF.rvt"));

        [Theory]
        [InlineData("BIM 360://Project/ARC.rvt")]
        [InlineData(@"C:\Users\Rafi\DC\ACCDocs\ALEC\ARC.rvt")]
        [InlineData("Autodesk Docs://ARC.rvt")]
        [InlineData("Autodesk Docs://ALEC/../../Windows/ARC.rvt")]
        [InlineData("")]
        public void CloudRelativePath_EmptyWhenNotMappable(string path) => Assert.Equal("", AccPath.CloudRelativePath(path));

        [Fact]
        public void FolderDisplay_TrimsCloudFolder() =>
            Assert.Equal(@"…\Models\Tower", FolderDisplay.Trim(@"02_ARC\Models\Tower", FolderDisplayMode.LastN, 2));
    }
}
