﻿using System;

namespace CryEngine.Utilities
{
    internal struct PointerWrapper
    {
        public PointerWrapper(IntPtr pointer)
        {
            ptr = pointer;
        }

        public IntPtr ptr;
    }
}