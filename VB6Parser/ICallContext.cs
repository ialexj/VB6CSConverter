using Antlr4.Runtime.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static VB6Parser.VisualBasic6Parser;

namespace VB6Parser
{
    public interface ICallStatementContext
    {
        ICallContext procedureCall();

        IMemberProcedureCallContext memberProcedureCall();
    }

    public interface ICallContext : IRuleNode, IParseTree, ITree
    {
        IIdentifierContext identifier();

        TypeHintContext typeHint();

        ArgsCallContext[] args();

        DictionaryCallStmtContext dictionaryCallStmt();
    }

    public interface IMemberProcedureCallContext : ICallContext
    {
        ImplicitCallStmt_InStmtContext implicitCallStmt_InStmt();
    }


    public partial class VisualBasic6Parser
    {
        public partial class ImplicitCallStmt_InBlockContext : ICallStatementContext
        {
            public ICallContext procedureCall() => iCS_B_ProcedureCall();

            public IMemberProcedureCallContext memberProcedureCall() => iCS_B_MemberProcedureCall();
        }

        public partial class ExplicitCallStmtContext : ICallStatementContext
        {
            public ICallContext procedureCall() => eCS_ProcedureCall();

            public IMemberProcedureCallContext memberProcedureCall() => eCS_MemberProcedureCall();
        }


        public partial class ECS_ProcedureCallContext : ICallContext 
        {
            public IIdentifierContext identifier() => ambiguousIdentifier();

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];
        }

        public partial class ICS_B_ProcedureCallContext : ICallContext
        {
            public IIdentifierContext identifier() => certainIdentifier();

            public TypeHintContext typeHint() => null;

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];
        }
        

        public partial class ECS_MemberProcedureCallContext : IMemberProcedureCallContext
        {
            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];
        }

        public partial class ICS_B_MemberProcedureCallContext : IMemberProcedureCallContext
        {
            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];
        }


        public partial class ICS_S_VariableOrProcedureCallContext : ICallContext
        {
            public ArgsCallContext[] args() => [];

            public IIdentifierContext identifier() => ambiguousIdentifier();
        }

        public partial class ICS_S_ProcedureOrArrayCallContext : ICallContext
        {
            public IIdentifierContext identifier() => ambiguousIdentifier();

            public ArgsCallContext[] args() => argsCall();

        }


        public partial class ICS_S_NestedProcedureCallContext : ICallContext
        {
            public ArgsCallContext[] args() => argsCall() is ArgsCallContext a ? [a] : [];

            public DictionaryCallStmtContext dictionaryCallStmt() => null;

            public IIdentifierContext identifier() => ambiguousIdentifier();
        }


        public partial class ICS_S_MembersCallContext : IMemberCallContext { }

        public partial class ICS_S_MemberCallContext : IMemberCallContext
        {
            public ICS_S_MemberCallContext[] iCS_S_MemberCall() => [];
        }
    }

    public interface IMemberCallContext
    {
        ICS_S_VariableOrProcedureCallContext iCS_S_VariableOrProcedureCall();

        ICS_S_ProcedureOrArrayCallContext iCS_S_ProcedureOrArrayCall();

        ICS_S_MemberCallContext[] iCS_S_MemberCall();
    }
}
