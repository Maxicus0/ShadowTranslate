using System;
using System.Reflection;
using UnityEngine;

namespace XUnity.AutoTranslator.Plugin.Core;

public static class Input
{

   private static Type _inputType;
   private static Type GetInputType()
   {
      _inputType ??= Type.GetType( "UnityEngine.Input, UnityEngine.CoreModule" );
      _inputType ??= Type.GetType( "UnityEngine.Input, UnityEngine.InputLegacyModule" );
      return _inputType ?? throw new InvalidOperationException( "Failed to find the UnityEngine.Input type" );
   }

   private static MethodInfo _getKey;
   public static bool GetKey( KeyCode key )
   {
      _getKey ??= GetInputType().GetMethod( "GetKey", [ typeof( KeyCode ) ] )!;
      return (bool)_getKey.Invoke( null, [ key ] )!;
   }

   private static MethodInfo _getKeyDown;
   public static bool GetKeyDown( KeyCode key )
   {
      _getKeyDown ??= GetInputType().GetMethod( "GetKeyDown", [ typeof( KeyCode ) ] )!;
      return (bool)_getKeyDown.Invoke( null, [ key ] )!;
   }

   private static MethodInfo _getMouseButton;
   public static bool GetMouseButton( int button )
   {
      _getMouseButton ??= GetInputType().GetMethod( "GetMouseButton", [ typeof( int ) ] )!;
      return (bool)_getMouseButton.Invoke( null, [ button ] )!;
   }

   private static MethodInfo _getMouseButtonDown;
   public static bool GetMouseButtonDown( int button )
   {
      _getMouseButtonDown ??= GetInputType().GetMethod( "GetMouseButtonDown", [ typeof( int ) ] )!;
      return (bool)_getMouseButtonDown.Invoke( null, [ button ] )!;
   }

   private static MethodInfo _resetInputAxes;
   public static void ResetInputAxes()
   {
      _resetInputAxes ??= GetInputType().GetMethod( "ResetInputAxes", [ ] )!;
      _resetInputAxes.Invoke( null, [ ] );
   }

   private static PropertyInfo _mouseScrollDelta;
   public static Vector2 mouseScrollDelta
   {
      get
      {
         _mouseScrollDelta ??= GetInputType().GetProperty( "mouseScrollDelta" )!;
         return (Vector2)_mouseScrollDelta.GetValue( null, null )!;
      }
   }

   private static PropertyInfo _mousePosition;
   public static Vector3 mousePosition
   {
      get
      {
         _mousePosition ??= GetInputType().GetProperty( "mousePosition" )!;
         return (Vector3)_mousePosition.GetValue( null, null )!;
      }
   }
}
