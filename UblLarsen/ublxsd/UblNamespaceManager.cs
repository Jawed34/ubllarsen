﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;
using ublxsd.Extensions;

namespace ublxsd
{
    /// <summary>
    /// Change to: Give a list of possible cs filesnamespaces to generate. The ones that dont have any codeDelcs just save empty files.
    /// Must join some files that have the same namespace prefix, like Xades.
    /// What "usings" to add can also be an issue. cbcOptimized vs not.
    /// Why not use just Common and Maindoc namespaces???
    /// old comment:
    /// What in short: given a xmlnamespace, should return a csharp namespace with a list of using directives. 
    /// More or less the topmost part of a csharp code file.
    /// </summary>
    class UblNamespaceManager
    {
        private readonly string nsHeaderComment = @"------------------------------------------------------------------------------
 <auto-generated>
     This code was generated by a tool.
     
     Changes to this file may cause incorrect behavior and will be lost if
     the code is regenerated.

     https://github.com/Gammern/ubllarsen
     {0}
 </auto-generated>
------------------------------------------------------------------------------";
        Dictionary<string, string[]> codeNamespaceUsings;
        // Hardcoded C# using statement resolver 
        Dictionary<string, string[]> codeNamespaceUsingsNonOptimized = new Dictionary<string, string[]>
        {
            [""] = new[] { "Cbc", "Cac", "Ext" },
            ["Cbc"] = new[] { "Udt" },
            ["Cac"] = new[] { "Udt", "Qdt", "Cbc" },
            ["Ext"] = new[] { "Udt", "Cbc" },
            ["Qdt"] = new[] { "Udt" },
            ["Udt"] = new[] { "Sbc", "Ext", "Cctscct", "Cbc" },
            ["Sbc"] = new[] { "Udt" }, // recursion
            ["Cctscct"] = new[] { "Udt", "Sbc", "Ext", "Cbc" },
            ["Abs"] = new[] { "Udt", "Ext", "Cbc" }, // Cbc for basedoc
            ["Xades"] = new[] { "DS" },
            ["Sac"] = new[] { "Udt", "Sbc", "DS" },
            ["Csc"] = new[] { "Sac" }
        };

        Dictionary<string, string[]> codeNamespaceUsingsOptimized = new Dictionary<string, string[]>
        {
            [""] = new[] { "Cac", "Udt" },
            ["Cac"] = new[] { "Udt" },
            ["Ext"] = new[] { "Udt" },
            ["Udt"] = new[] { "Sbc", "Ext", "Cctscct" },
            ["Sbc"] = new[] { "Udt" },
            ["Cctscct"] = new[] { "Udt", "Sbc", "Ext" },
            ["Abs"] = new[] { "Udt", "Ext" }, // Hack for basedoc
            ["Xades"] = new[] { "DS" },
            ["Sac"] = new[] { "Udt", "Sbc", "DS" },
            ["Csc"] = new[] { "Sac" }
        };

        Dictionary<string, string> xml2CSharpNamespaceDictionary;
        private XmlSchemaSet schemaSet;
        private string csDefaultNamespace;
        private bool OptionOptimizeCommonBasicComponents;
        static string[] unwantedPrefixes = new string[] { "", "xsd", "abs", "cct" }; //,"ccts-cct" ,"ds", "xades" };


        /// <summary>
        /// </summary>
        public UblNamespaceManager(XmlSchemaSet schemaSet, string csDefaultNamespace, bool optimizeCommonBasicComponents)
        {
            this.schemaSet = schemaSet;
            this.csDefaultNamespace = csDefaultNamespace;
            this.OptionOptimizeCommonBasicComponents = optimizeCommonBasicComponents;

            // Build a xml namespace(key) to csharp codenamespace/scope(value) dictionary by looking at all schema root xmlns attributes
            // Will bomb out on Distinct() if schemas use different namespace prefixes for the same namespace (empty ones are removed)
            xml2CSharpNamespaceDictionary = schemaSet.Schemas().Cast<XmlSchema>()
                .SelectMany(schema => schema.Namespaces.ToArray().Where(qname => !unwantedPrefixes.Contains(qname.Name)))
                .Select(qname => new { qname.Namespace, qname.Name })
                .Distinct()
                .ToDictionary(key => key.Namespace, val => $"{csDefaultNamespace}.{ CodeIdentifier.MakePascal(val.Name)}");

            // missing references in 2.1. Is it unused?
            xml2CSharpNamespaceDictionary.Add(Constants.CommonSignatureComponentsTargetNamespace, $"{csDefaultNamespace}.Csc");
            xml2CSharpNamespaceDictionary[Constants.Xadesv132TargetNamespace] = $"{csDefaultNamespace}.Xades";
            xml2CSharpNamespaceDictionary[Constants.Xadesv141TargetNamespace] = $"{csDefaultNamespace}.Xades";// 141"; // Probably incorrect

            // add key:xmlns value:cSharpScope for all maindocs. They all point to the same scope value string
            foreach (XmlSchema schema in schemaSet.MaindocSchemas().Cast<XmlSchema>())
            {
                string targetNamespace = schema.TargetNamespace;
                if (!xml2CSharpNamespaceDictionary.ContainsKey(targetNamespace))
                {
                    xml2CSharpNamespaceDictionary.Add(targetNamespace, csDefaultNamespace);
                }
            }

            // using directives in csharp files may vary depending on optimising of types
            if (this.OptionOptimizeCommonBasicComponents)
            {
                codeNamespaceUsings = codeNamespaceUsingsOptimized;
            }
            else
            {
                codeNamespaceUsings = codeNamespaceUsingsNonOptimized;
            }
            // prepend dictionary keys with default C# code namespace separated by a dot.
            codeNamespaceUsings = codeNamespaceUsings.ToDictionary(k => csDefaultNamespace + (k.Key == "" ? "" : ".") + k.Key, v => v.Value);
        }

        public CodeNamespace GetCodeNamespaceForXmltargetNamespace(string xmlNamespace)
        {
            if (xml2CSharpNamespaceDictionary.ContainsKey(xmlNamespace))
            {
                string csScopeName = xml2CSharpNamespaceDictionary[xmlNamespace];
                CodeNamespace codeNs = new CodeNamespace(csScopeName);
                string commentLine = GetCommentTextForScope(csScopeName);
                CodeComment headerComment = new CodeComment(string.Format(nsHeaderComment, commentLine));
                CodeCommentStatement fileHeader = new CodeCommentStatement(headerComment);
                codeNs.Comments.Add(fileHeader);

                // HACK! Do a hardcore swap if it is basedoc
                if (xmlNamespace.Equals(Constants.BaseDocumentTargetNamespace))
                {
                    csScopeName += ".Abs";
                }

                // figure out what using statements to add
                if (codeNamespaceUsings.ContainsKey(csScopeName))
                {
                    foreach (string usingNamespace in codeNamespaceUsings[csScopeName])
                    {
                        codeNs.Imports.Add(new CodeNamespaceImport(usingNamespace));
                    }
                }
                return codeNs;
            }
            else
            {
                //throw new ApplicationException(string.Format("Don't know how to handle xml namespace {0}", xmlNamespace));
                Console.WriteLine($"Don't know how to handle xml namespace {xmlNamespace}");
                return new CodeNamespace("BogusDontKnowHowToHandle");
            }
        }

        private string GetCommentTextForScope(string csScopeName)
        {
            string res = string.Empty;
            if (csScopeName.EndsWith(".Cbc"))
            {
                res = " UBL BBIEs (Basic Business Information Entities) are the leaf nodes of every UBL document structure.";
                if (this.OptionOptimizeCommonBasicComponents)
                {
                    res = res + Environment.NewLine + " Types in this scope has been optimized/replaced by types from Udt."
                        + Environment.NewLine + " Members of maindocs streamed under Cbc namespace will in fact be Udt types.";
                }
                else
                {
                    res = res + Environment.NewLine + " Yagni-types in this scope do not have any documentation present in xsd files.";
                }
            }
            else if (csScopeName.EndsWith(".Cac"))
            {
                res = " UBL ASBIEs (Association Business Information Entities) are substructures of an UBL document.";
            }
            else if (csScopeName.EndsWith(".Cctscct"))
            {
                res = " Types at the lowest level have been made abstract and prefixed with \"cctscct\" to avoid naming conflicts.";
            }
            else if (csScopeName.EndsWith(".Qdt"))
            {
                res = " --no qualified data types defined at this time--";
            }
            if (res != string.Empty) res = Environment.NewLine + res + Environment.NewLine;
            return res;
        }

        /// <summary>
        /// Return a list of all schemas for the purpose of generating matching c# files. 
        /// Some schemas will not contain any CodeTypeDeclaration/generated code, and hence, c# file will be empty with a header only.
        /// </summary>
        public IEnumerable<XmlSchema> Schemas
        {
            get
            {
                return this.schemaSet.Schemas(). Cast<XmlSchema>();
            }
        }
    }
}
