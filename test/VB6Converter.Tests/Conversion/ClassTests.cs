using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using static VB6Converter.Tests.Validations;
using VB6Parser;

namespace VB6Converter.Tests.Conversion;

[TestClass]
public sealed class ClassTests
{
    [TestMethod]
    public void InferConst() => ValidateClassMatches(
        """
        Public Const opNone = 0
        Public Const opTest = "String"
        """,

        """
        public static partial class Test
        {
            public const int opNone = 0;
            public const string opTest = "String";
        }
        """,

        "Test");

    [TestMethod]
    public void Struct() => ValidateMemberMatches(
        """
        Private Type INITCOMMONCONTROLSEX_TYPE
            dwSize As Long
            dwICC As Long
        End Type
        """,
        """
        private struct INITCOMMONCONTROLSEX_TYPE
        {
            public int dwSize;
            public int dwICC;
        }
        """);

    [TestMethod]
    public void Enum() => ValidateMemberMatches(
        """
        Public Enum PrintersLocation
            PRINTERS_LOCAL_MACHINE = 0
            PRINTERS_CURRENT_USER = 1
        End Enum
        """,
        """
        public enum PrintersLocation
        {
            PRINTERS_LOCAL_MACHINE = 0,
            PRINTERS_CURRENT_USER = 1
        }
        """);

    [TestMethod]
    public void Extern() => ValidateMemberMatches(
        """
        Private Declare Function GetSystemMetrics Lib "user32" (ByVal nIndex As Long) As Long
        """,
        """
        [DllImport("user32")]
        private static extern int GetSystemMetrics(int nIndex);
        """);

    [TestMethod]
    public void ExternWithAlias() => ValidateMemberMatches(
        """
        Private Declare Function GetComputerName Lib "kernel32" Alias "GetComputerNameA" (ByVal lpBuffer As String, nSize As Long) As Long
        """,
        """
        [DllImport("kernel32", EntryPoint = "GetComputerNameA")]
        private static extern int GetComputerName(string lpBuffer, int nSize);
        """);

    [TestMethod]
    public void MethodArguments() => ValidateMemberMatches(
        """
        Public Sub Test(ByVal arg1 As String, ByRef arg2 As Long, ByVal another As Variant)
        End Sub
        """,
        """
        public static void Test(string arg1, int arg2, dynamic another)
        {
        }
        """
    );

    [TestMethod]
    public void FunctionReturn() => ValidateMemberMatches(
        """
        Public Function Test() As Boolean
            If SomeVariable Then Test = True
            DoSomethingElse
            If SomeOtherVariable Then Exit Function
        End Function
        """,
        """
        public static bool Test()
        {
            bool Test = default;
            if (SomeVariable)
                Test = true;
            DoSomethingElse();
            if (SomeOtherVariable)
                return Test;
            return Test;
        }
        """
    );

    [TestMethod]
    public void PropertyExpression() => ValidateMemberMatches(
        """
        Public Property Get Test() As String
            Test = testVar
        End Property

        Public Property Let Test(ByVal someValue As String)
            testVar = someValue
        End Property
        """,
        """
        public static string Test
        {
            get => testVar;
            set
            {
                testVar = value;
            }
        }
        """);

    [TestMethod]
    public void PropertyMethodLastReturn() => ValidateMemberMatches(
        """
        Public Property Get Test() As String
            SomeMethod
            Test = testVar
        End Property

        Public Property Let Test(ByVal someValue As String)
            testVar = someValue
        End Property
        """,
        """
        public static string Test
        {
            get
            {
                SomeMethod();
                return testVar;
            }

            set
            {
                testVar = value;
            }
        }
        """);

    [TestMethod]
    public void PropertyMethodFull() => ValidateMemberMatches(
        """
        Public Property Get Test() As String
            SomeMethod
            Test = testVar
            SomeMethod
        End Property

        Public Property Let Test(ByVal someValue As String)
            testVar = someValue
        End Property
        """,
        """
        public static string Test
        {
            get
            {
                string Test = default;
                SomeMethod();
                Test = testVar;
                SomeMethod();
                return Test;
            }

            set
            {
                testVar = value;
            }
        }
        """);

    [TestMethod]
    public void PropertyLetParameterNamedVisible_DoesNotRenameMemberAccess() => ValidateMemberMatches(
        """
        Private Property Let ReciboVisivel(Visible As Boolean)
               lblDocumento(19).Visible = Visible
               chkRecibo.Visible = Visible
               txtRecibo.Visible = Visible
        End Property
        """,
        """
        private static bool ReciboVisivel
        {
            set
            {
                lblDocumento[19].Visible = value;
                chkRecibo.Visible = value;
                txtRecibo.Visible = value;
            }
        }
        """);

    [TestMethod]
    public void ParameterizedPropertyGetter() => ValidateMemberMatches(
        """
        Public Property Get NotaEncomenda(ByVal NewTipoNE As Byte, ByVal NewIDNE As Long) As Long
            NotaEncomenda = 0
        End Property
        """,
        """
        // VB6 multi-value property getter
        public static int NotaEncomenda(byte NewTipoNE, int NewIDNE) => 0;
        """);

    [TestMethod]
    public void ParameterizedPropertySetter() => ValidateMemberMatches(
        """
        Public Property Let NotaEncomenda(ByVal NewTipoNE As Byte, ByVal NewIDNE As Long, ByVal NewIdFrn As Long)
        End Property
        """,
        """
        // VB6 multi-value property setter
        public static void SetNotaEncomenda(byte NewTipoNE, int NewIDNE, int NewIdFrn)
        {
        }
        """);

    [TestMethod]
    public void Variables() => ValidateClassMatches(
        """
        Private str As String
        Private int1 As Long, int2 as Long
        Public arr1() As Long
        Public arr2(1 to 10) As Long
        Public arr3(1 To 10, 1 To 20) As Long
        """,
        """
        public static partial class Variables
        {
            private static string str;
            private static int int1;
            private static int int2;
            public static int[] arr1;
            public static int[] arr2 = new int[10 + 1];
            public static int[, ] arr3 = new int[10 + 1, 20 + 1];
        }
        """);

    [TestMethod]
    public void Event() => ValidateClassMatches(
        """
        Public Event TotalChanged()
        """,
        """
        public static partial class Event
        {
            public static event EventHandler TotalChanged;
        }
        """);

    [TestMethod]
    public void EnumerableClass_ImplementsGenericAndExplicitNonGenericIEnumerable()
    {
        var vb = """
        Public Property Get NewEnum() As IUnknown
            Set NewEnum = Nothing
        End Property
        """;

        var conversion = VB6ToCSharpConversion.ConvertString(vb, "EnumerableClass", type: VisualBasicFileType.Class);

        conversion.ParseErrors.Should().BeEmpty();
        conversion.TransformErrors.Should().BeEmpty();
        conversion.SyntaxErrors.Should().BeEmpty();

        var actual = conversion.Class.NormalizeWhitespace().ToFullString();
        actual.Should().Be("""
            public partial class EnumerableClass : System.Collections.IEnumerable, System.Collections.Generic.IEnumerable<dynamic>
            {
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
                public System.Collections.Generic.IEnumerator<dynamic> GetEnumerator() => throw new System.NotImplementedException();
            }
            """);
    }

}
