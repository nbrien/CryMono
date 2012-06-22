﻿using System;

namespace CryEngine
{
	/// <summary>
	/// Used for non-CryMono entities, i.e. Lua / C++ such.
	/// </summary>
	[ExcludeFromCompilation]
	internal class NativeEntity : Entity
	{
		public NativeEntity() { }
		public NativeEntity(EntityId id, IntPtr ptr)
		{ 
			Id = id;
			EntityPointer = ptr;
		}
	}
}