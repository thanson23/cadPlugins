using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace PvGrade
{
    public class Plugin : IExtensionApplication
    {
        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("Tolerance_Test")]
        public void ToleranceCommand()
        {
            Lobf.ToleranceCommand();
        }
    }
}

namespace LabelTest
{
    public class Plugin : IExtensionApplication
    {
        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("LabelTextZ")]
        public void LabelCommand()
        {
            LabelText.LabelTextZ();
        }
        [CommandMethod("LabelContour")]
        public void LabelCont()
        {
            LabelText.LabelContour();
        }
    }
}

namespace vmrControl
{
    public class Plugin : IExtensionApplication
    {
        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("vmrControl")]
        public void LabelCommand()
        {
            vmrControl.LabelControl();
        }
    }
}



namespace template
{
    public class Plugin : IExtensionApplication
    {
        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        [CommandMethod("nameOfCommandToTypeIntoCad")]
        public void LabelCommand()
        {
            templateClass.mainFunctionName();
        }
    }
}
