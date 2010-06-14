﻿#region Copyright & License Information
/*
 * Copyright 2007,2009,2010 Chris Forbes, Robert Pepperell, Matthew Bowra-Dean, Paul Chote, Alli Witheford.
 * This file is part of OpenRA.
 * 
 *  OpenRA is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 * 
 *  OpenRA is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 * 
 *  You should have received a copy of the GNU General Public License
 *  along with OpenRA.  If not, see <http://www.gnu.org/licenses/>.
 */
#endregion

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OpenRA.FileFormats;

namespace OpenRA
{
	public class SyncAttribute : Attribute { }

	public static class Sync
	{
		static Cache<Type, Func<object, int>> hashFuncCache = new Cache<Type, Func<object, int>>( t => GenerateHashFunc( t ) );

		public static int CalculateSyncHash( object obj )
		{
			return hashFuncCache[ obj.GetType() ]( obj );
		}

		static void EmitSyncOpcodes(Type type, ILGenerator il)
		{
			if (type == typeof(int))
			{
				il.Emit(OpCodes.Xor);
			}
			else if (type == typeof(bool))
			{
				var l = il.DefineLabel();
				il.Emit(OpCodes.Ldc_I4, 0xaaa);
				il.Emit(OpCodes.Brtrue, l);
				il.Emit(OpCodes.Pop);
				il.Emit(OpCodes.Ldc_I4, 0x555);
				il.MarkLabel(l);
				il.Emit(OpCodes.Xor);
			}
			else if (type == typeof(int2))
			{
				il.EmitCall(OpCodes.Call, ((Func<int2, int>)hash_int2).Method, null);
				il.Emit(OpCodes.Xor);
			}
			else if (type == typeof(TypeDictionary))
			{
				il.EmitCall(OpCodes.Call, ((Func<TypeDictionary, int>)hash_tdict).Method, null);
				il.Emit(OpCodes.Xor);
			}
			else if (type == typeof(Actor))
			{
				il.EmitCall(OpCodes.Call, ((Func<Actor, int>)hash_actor).Method, null);
				il.Emit(OpCodes.Xor);
			}
			else if (type == typeof(Player))
			{
				il.EmitCall(OpCodes.Call, ((Func<Player, int>)hash_player).Method, null);
				il.Emit(OpCodes.Xor);
			}
			else if (type.HasAttribute<SyncAttribute>())
			{
				il.EmitCall(OpCodes.Call, ((Func<object, int>)CalculateSyncHash).Method, null);
				il.Emit(OpCodes.Xor);
			}
			else
				throw new NotImplementedException("SyncAttribute on member of unhashable type: {0}".F(type.FullName));
		}

		public static Func<object, int> GenerateHashFunc(Type t)
		{
			var d = new DynamicMethod("hash_{0}".F(t.Name), typeof(int), new Type[] { typeof(object) }, t);
			var il = d.GetILGenerator();
			var this_ = il.DeclareLocal(t).LocalIndex;
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, t);
			il.Emit(OpCodes.Stloc, this_);
			il.Emit(OpCodes.Ldc_I4_0);

			const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
			foreach (var field in t.GetFields(bf).Where(x => x.HasAttribute<SyncAttribute>()))
			{
				il.Emit(OpCodes.Ldloc, this_);
				il.Emit(OpCodes.Ldfld, field);

				EmitSyncOpcodes(field.FieldType, il);
			}

			foreach (var prop in t.GetProperties(bf).Where(x => x.HasAttribute<SyncAttribute>()))
			{
				il.Emit(OpCodes.Ldloc, this_);
				il.EmitCall(OpCodes.Call, prop.GetGetMethod(), null);

				EmitSyncOpcodes(prop.PropertyType, il);
			}

			il.Emit(OpCodes.Ret);
			return (Func<object, int>)d.CreateDelegate(typeof(Func<object, int>));
		}

		public static int hash_int2( int2 i2 )
		{
			return ( ( i2.X * 5 ) ^ ( i2.Y * 3 ) ) / 4;
		}

		public static int hash_tdict( TypeDictionary d )
		{
			int ret = 0;
			foreach( var o in d )
				ret += CalculateSyncHash( o );
			return ret;
		}

		public static int hash_actor( Actor a )
		{
			if( a != null )
				return (int)( a.ActorID << 16 );
			return 0;
		}

		public static int hash_player( Player p )
		{
			if( p != null )
				return p.Index * 0x567;
			return 0;
		}

		static bool HasAttribute<T>( this MemberInfo mi )
		{
			return mi.GetCustomAttributes(typeof(T), true).Length != 0;
		}
	}
}
