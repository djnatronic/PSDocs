﻿using PSDocs.Data;
using PSDocs.Models;
using PSDocs.Processor;
using PSDocs.Processor.Markdown;
using PSDocs.Runtime;
using System.Collections.Generic;
using System.Management.Automation;

namespace PSDocs.Pipeline
{
    public interface IInvokePipelineBuilder : IPipelineBuilder
    {
        void InstanceName(string[] instanceName);
    }

    internal sealed class InvokePipelineBuilder : PipelineBuilderBase, IInvokePipelineBuilder
    {
        private string[] _InstanceName;

        internal InvokePipelineBuilder(Source[] source, HostContext hostContext)
            : base(source, hostContext)
        {
            // Do nothing
        }

        public void InstanceName(string[] instanceName)
        {
            if (instanceName == null || instanceName.Length == 0)
                return;

            _InstanceName = instanceName;
        }

        public override IPipeline Build()
        {
            return new InvokePipeline(PrepareContext(), Source);
        }

        protected override PipelineContext PrepareContext()
        {
            var instanceNameBinder = new InstanceNameBinder(_InstanceName);
            var context = new PipelineContext(Option, Writer, OutputVisitor, instanceNameBinder);
            return context;
        }
    }

    internal sealed class InvokePipeline : PipelineBase, IPipeline
    {
        private readonly PipelineStream _Stream;

        private IDocumentBuilder[] _Builder;
        private MarkdownProcessor _Processor;
        private RunspaceContext _Runspace;

        internal InvokePipeline(PipelineContext context, Source[] source)
            : base(context, source)
        {
            _Stream = new PipelineStream(context);
            Prepare();
        }

        public override void Process(PSObject sourceObject)
        {
            //if (sourceObject != null)
            //{
            _Stream.Enqueue(sourceObject);
            //}

            while (!_Stream.IsEmpty && _Stream.TryDequeue(out PSObject nextObject))
                ProcessObject(nextObject);
        }

        private void ProcessObject(PSObject sourceObject)
        {
            try
            {
                var doc = BuildDocument(sourceObject);
                for (var i = 0; i < doc.Length; i++)
                {
                    var result = WriteDocument(doc[i]);
                    if (result != null)
                        Context.WriteOutput(result);
                }
            }
            finally
            {
                _Runspace.ExitTargetObject();
            }
        }

        private IDocumentResult WriteDocument(Document document)
        {
            return _Processor.Process(Context.Option, document);
        }

        internal Document[] BuildDocument(PSObject sourceObject)
        {
            _Runspace.EnterTargetObject(sourceObject);
            var result = new List<Document>();
            for (var c = 0; c < Context.Option.Output.Culture.Length; c++)
            {
                _Runspace.EnterCulture(Context.Option.Output.Culture[c]);
                for (var i = 0; i < _Builder.Length; i++)
                {
                    foreach (var instanceName in Context.InstanceNameBinder.GetInstanceName(_Builder[i].Name))
                    {
                        _Runspace.InstanceName = instanceName;

                        // TODO: Add target name binding
                        var document = _Builder[i].Process(_Runspace, sourceObject);
                        result.Add(document);
                    }
                }
            }
            return result.ToArray();
        }

        private void Prepare()
        {
            _Runspace = new RunspaceContext(Context, Source);
            _Builder = HostHelper.GetDocumentBuilder(_Runspace, Source);
            _Processor = new MarkdownProcessor();
        }

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _Processor = null;
                if (_Builder != null)
                {
                    for (var i = 0; i < _Builder.Length; i++)
                        _Builder[i].Dispose();

                    _Builder = null;
                }
                _Runspace.Dispose();
                _Runspace = null;
            }
            base.Dispose(disposing);
        }

        #endregion IDisposable
    }
}
