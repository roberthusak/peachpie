﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Roslyn.Utilities;

namespace Pchp.CodeAnalysis.Symbols
{
    class SynthesizedFieldSymbol : FieldSymbol
    {
        readonly NamedTypeSymbol _containing;
        TypeSymbol _type;
        readonly string _name;
        readonly Accessibility _accessibility;
        readonly bool _isStatic, _isReadOnly;
        readonly ConstantValue _const;

        public SynthesizedFieldSymbol(
            NamedTypeSymbol containing,
            TypeSymbol type,
            string name,
            Accessibility accessibility,
            bool isStatic = false,
            bool isReadOnly = false)
        {
            _containing = containing;
            _name = name;
            _type = type;
            _accessibility = accessibility;
            _isStatic = isStatic;
            _isReadOnly = isReadOnly;
        }

        public SynthesizedFieldSymbol(
            NamedTypeSymbol containing,
            TypeSymbol type,
            string name,
            Accessibility accessibility,
            ConstantValue constant)
            :this(containing, type, name, accessibility, true)
        {
            _const = constant;
        }

        public override Symbol AssociatedSymbol => null;

        public override Symbol ContainingSymbol => _containing;

        public override ImmutableArray<CustomModifier> CustomModifiers => ImmutableArray<CustomModifier>.Empty;

        public override Accessibility DeclaredAccessibility => _accessibility;

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences => ImmutableArray<SyntaxReference>.Empty;

        public override bool IsConst => _const != null;

        public override bool IsReadOnly => _isReadOnly;   // .initonly

        public override bool IsStatic => _isStatic;

        public override bool IsVolatile => false;

        public override bool IsImplicitlyDeclared => true;

        public override ImmutableArray<Location> Locations => ImmutableArray<Location>.Empty;

        public override string Name => _name;

        internal override bool HasRuntimeSpecialName => false;

        internal override bool HasSpecialName => false;

        internal override bool IsNotSerialized => false;

        internal override MarshalPseudoCustomAttributeData MarshallingInformation => null;

        internal override ObsoleteAttributeData ObsoleteAttributeData => null;

        internal override int? TypeLayoutOffset => null;

        internal override ConstantValue GetConstantValue(bool earlyDecodingWellKnownAttributes) => _const;

        internal override TypeSymbol GetFieldType(ConsList<FieldSymbol> fieldsBeingBound) => _type;

        internal void SetFieldType(TypeSymbol type)
        {
            _type = type;
        }
    }
}
