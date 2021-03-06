﻿// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Builder;
using Unity.Builder.Selection;
using Unity.Builder.Strategy;
using Unity.Interception.InterceptionBehaviors;
using Unity.Interception.Interceptors;
using Unity.Policy;

namespace Unity.Interception.ContainerIntegration.ObjectBuilder
{
    /// <summary>
    /// A <see cref="IBuilderStrategy"/> that hooks up type interception. It looks for
    /// a <see cref="ITypeInterceptionPolicy"/> for the current build key, or the current
    /// build type. If present, it substitutes types so that that proxy class gets
    /// built up instead. On the way back, it hooks up the appropriate handlers.
    /// </summary>
    public class TypeInterceptionStrategy : BuilderStrategy
    {
        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PreBuildUp method is called when the chain is being executed in the
        /// forward direction.
        /// </summary>
        /// <remarks>In this class, PreBuildUp is responsible for figuring out if the
        /// class is proxyable, and if so, replacing it with a proxy class.</remarks>
        /// <param name="context">Context of the build operation.</param>
        public override void PreBuildUp(IBuilderContext context)
        {
            if ((context ?? throw new ArgumentNullException(nameof(context))).Existing != null)
            {
                return;
            }

            Type typeToBuild = context.BuildKey.Type;

            var interceptionPolicy = FindInterceptionPolicy<ITypeInterceptionPolicy>(context);
            if (interceptionPolicy == null)
            {
                return;
            }

            var interceptor = interceptionPolicy.GetInterceptor(context);
            if (!interceptor.CanIntercept(typeToBuild))
            {
                return;
            }

            var interceptionBehaviorsPolicy = FindInterceptionPolicy<IInterceptionBehaviorsPolicy>(context);

            IEnumerable<IInterceptionBehavior> interceptionBehaviors =
                interceptionBehaviorsPolicy == null
                    ?
                        Enumerable.Empty<IInterceptionBehavior>()
                    :
                        interceptionBehaviorsPolicy.GetEffectiveBehaviors(
                            context, interceptor, typeToBuild, typeToBuild)
                        .Where(ib => ib.WillExecute);

            IAdditionalInterfacesPolicy additionalInterfacesPolicy =
                FindInterceptionPolicy<IAdditionalInterfacesPolicy>(context);

            IEnumerable<Type> additionalInterfaces =
                additionalInterfacesPolicy != null ? additionalInterfacesPolicy.AdditionalInterfaces : Type.EmptyTypes;

            var enumerable = interceptionBehaviors as IInterceptionBehavior[] ?? interceptionBehaviors.ToArray();
            context.Policies.Set(
                new EffectiveInterceptionBehaviorsPolicy { Behaviors = enumerable },
                context.BuildKey);

            Type[] allAdditionalInterfaces =
                Intercept.GetAllAdditionalInterfaces(enumerable, additionalInterfaces);

            Type interceptingType =
                interceptor.CreateProxyType(typeToBuild, allAdditionalInterfaces);

            DerivedTypeConstructorSelectorPolicy.SetPolicyForInterceptingType(context, interceptingType);
        }

        /// <summary>
        /// Called during the chain of responsibility for a build operation. The
        /// PostBuildUp method is called when the chain has finished the PreBuildUp
        /// phase and executes in reverse order from the PreBuildUp calls.
        /// </summary>
        /// <remarks>In this class, PostBuildUp checks to see if the object was proxyable,
        /// and if it was, wires up the handlers.</remarks>
        /// <param name="context">Context of the build operation.</param>
        public override void PostBuildUp(IBuilderContext context)
        {
            IInterceptingProxy proxy = (context ?? throw new ArgumentNullException(nameof(context))).Existing as IInterceptingProxy;
            if (proxy == null)
            {
                return;
            }

            EffectiveInterceptionBehaviorsPolicy effectiveInterceptionBehaviorsPolicy =
                context.Policies.Get<EffectiveInterceptionBehaviorsPolicy>(context.BuildKey, true);
            if (effectiveInterceptionBehaviorsPolicy == null)
            {
                return;
            }

            foreach (var interceptionBehavior in effectiveInterceptionBehaviorsPolicy.Behaviors)
            {
                proxy.AddInterceptionBehavior(interceptionBehavior);
            }
        }

        private static TPolicy FindInterceptionPolicy<TPolicy>(IBuilderContext context)
            where TPolicy : class, IBuilderPolicy
        {
            return context.Policies.Get<TPolicy>(context.BuildKey, false) ??
                context.Policies.Get<TPolicy>(context.BuildKey.Type, false);
        }

        private class EffectiveInterceptionBehaviorsPolicy : IBuilderPolicy
        {
            public EffectiveInterceptionBehaviorsPolicy()
            {
                Behaviors = new List<IInterceptionBehavior>();
            }

            public IEnumerable<IInterceptionBehavior> Behaviors { get; set; }
        }

        private class DerivedTypeConstructorSelectorPolicy : IConstructorSelectorPolicy
        {
            private readonly Type _interceptingType;
            private readonly IConstructorSelectorPolicy _originalConstructorSelectorPolicy;

            private DerivedTypeConstructorSelectorPolicy(
                Type interceptingType,
                IConstructorSelectorPolicy originalConstructorSelectorPolicy)
            {
                _interceptingType = interceptingType;
                _originalConstructorSelectorPolicy = originalConstructorSelectorPolicy;
            }

            public SelectedConstructor SelectConstructor(IBuilderContext context, IPolicyList resolverPolicyDestination)
            {
                SelectedConstructor originalConstructor =
                    _originalConstructorSelectorPolicy.SelectConstructor(context, resolverPolicyDestination);

                return FindNewConstructor(originalConstructor, _interceptingType);
            }

            private static SelectedConstructor FindNewConstructor(SelectedConstructor originalConstructor, Type interceptingType)
            {
                ParameterInfo[] originalParams = originalConstructor.Constructor.GetParameters();

                ConstructorInfo newConstructorInfo =
                    interceptingType.GetConstructor(originalParams.Select(pi => pi.ParameterType).ToArray());

                SelectedConstructor newConstructor = new SelectedConstructor(newConstructorInfo);

                foreach (IDependencyResolverPolicy resolver in originalConstructor.GetParameterResolvers())
                {
                    newConstructor.AddParameterResolver(resolver);
                }

                return newConstructor;
            }

            public static void SetPolicyForInterceptingType(IBuilderContext context, Type interceptingType)
            {
                IPolicyList selectorPolicyDestination;
                var currentSelectorPolicy = context.Policies.Get<IConstructorSelectorPolicy>(context.BuildKey, out selectorPolicyDestination);
                var currentDerivedTypeSelectorPolicy = currentSelectorPolicy as DerivedTypeConstructorSelectorPolicy;

                if (currentDerivedTypeSelectorPolicy == null)
                {
                    selectorPolicyDestination.Set<IConstructorSelectorPolicy>(
                        new DerivedTypeConstructorSelectorPolicy(
                            interceptingType,
                            currentSelectorPolicy),
                        context.BuildKey);
                }
                else if (currentDerivedTypeSelectorPolicy._interceptingType != interceptingType)
                {
                    selectorPolicyDestination.Set<IConstructorSelectorPolicy>(
                        new DerivedTypeConstructorSelectorPolicy(
                            interceptingType,
                            currentDerivedTypeSelectorPolicy._originalConstructorSelectorPolicy),
                        context.BuildKey);
                }
            }
        }
    }
}
