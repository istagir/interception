﻿// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
using Unity.Builder;
using Unity.Interception.Interceptors.TypeInterceptors;

namespace Unity.Interception.ContainerIntegration.ObjectBuilder
{
    /// <summary>
    /// Interface that controls when and how types get intercepted.
    /// </summary>
    public interface ITypeInterceptionPolicy : IBuilderPolicy
    {
        /// <summary>
        /// Interceptor to use to create type proxy
        /// </summary>
        /// <param name="context">Context for current build operation.</param>
        ITypeInterceptor GetInterceptor(IBuilderContext context);

        /// <summary>
        /// Cache for proxied type.
        /// </summary>
        Type ProxyType { get; set; }
    }
}
