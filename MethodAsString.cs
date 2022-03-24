
public static class MethodAsString {
    private static string GetSerializableMethodName(MethodInfo method) {
        StringBuilder sb = new StringBuilder();

        if (method.IsStatic || (GetTypeEnum(method.DeclaringType) == GetTypeEnum(method.ReflectedType))) {
            sb.Append(GetSerializableTypeName(method.DeclaringType));
        }
        else {
            sb.Append(GetSerializableTypeName(method.ReflectedType));
        }

        sb.Append(';');

        sb.Append(method.Name);

        foreach (var p in method.GetParameters()) {
            sb.Append(';');
            sb.Append(GetSerializableTypeName(p.ParameterType));
        }

        Type[] genericArguments = method.GetGenericArguments();
        if (genericArguments.Length > 0) {

            MethodInfo definition = method.GetGenericMethodDefinition();
            ParameterInfo[] definitionParameters = definition.GetParameters();
            Type[] definitionGenericArguments = definition.GetGenericArguments();

            sb.Append(';');
            foreach (var p in definitionParameters) {
                Type type = p.ParameterType;
                if (type.IsGenericParameter) {
                    if (type == definitionGenericArguments[0]) sb.Append('T');
                    else if (type == definitionGenericArguments[1]) sb.Append('U');
                    else if (type == definitionGenericArguments[2]) sb.Append('V');
                    else if (type == definitionGenericArguments[3]) sb.Append('W');
                }
                else if (type.ContainsGenericParameters) {
                    sb.Append('t');
                }
                else {
                    sb.Append('s');
                }
            }
            sb.Append('*');

            foreach (var t in genericArguments) {
                sb.Append(';');
                sb.Append(GetSerializableTypeName(t));
            }
        }
        return sb.ToString();
    }

    public static string GetSerializableTypeName(Type type) {
        if (type == null) return typeof(object).FullName;
        string typeName = type.AssemblyQualifiedName;
        if (typeName == null) {
            return type.FullName ?? type.Name;
        }
        for (int i = 0; i < TypeNameReplaced.Length; i++) {
            string key = TypeNameReplaced[i];
            while (typeName.Contains(key)) {
                int startIndex = typeName.IndexOf(key);
                int count = typeName.Length - startIndex;
                for (int j = startIndex + key.Length; j < typeName.Length; j++) {
                    if (typeName[j] == ']' || typeName[j] == ',') {
                        count = j - startIndex;
                        break;
                    }
                }
                if (count > 0) {
                    string newTypeName = typeName.Replace(typeName.Substring(startIndex, count), TypeNameReplacing[i]);
                    if (Type.GetType(typeName) == null) break;
                    typeName = newTypeName;
                }
            }
        }

        //last resort attempt at serialization. In 99.9% of cases this shouldn't happen
        if (Type.GetType(typeName) == null) typeName = type.AssemblyQualifiedName;
        return typeName;
    }

    public static MethodInfo GetMethod(string signature) {

        MethodInfo mi = null;

        string[] data = signature.Split(';');

        //method definition is made up of the following data entries which are separated by ';' character:
        //[method_name] - method name
        //[reflected_type] - type that defines this method
        //[type_1]...[type_n] - array of method parameter types
        //[ttt*] - array of chars that defines wat kind of parameter this is (specific or generic), this data entry always ends with '*' char - only for generic methods
        //[type_1]...[type_n] - array of method generic arguments - only for generic methods

        int parameterCount = data.Length - 2;
        int genericCount = 0;

        if (parameterCount < 0) {
            return null;
        }

        //look if method is generic
        for (int i = 2; i < data.Length; i++) {
            //if we on a data entry that ends with '*' character then after it generic arguments are defined
            if (data[i][data[i].Length - 1] == '*') {
                parameterCount = i - 2;
                genericCount = data.Length - i - 1;
                break;
            }
        }

        bool methodIsGeneric = genericCount > 0;

        Type[] parameterTypes = new Type[parameterCount];

        for (int i = 0; i < parameterCount; i++) {
            Type parameterType = Type.GetType(data[i + 2]);

            if (!methodIsGeneric) {
                parameterTypes[i] = parameterType;
            }
            else {
                switch (data[data.Length - genericCount - 1][i]) {
                    default:
                    //specific serializable type
                    case 's':
                        parameterTypes[i] = parameterType;
                        break;
                    //generic unserializable type (generic argument 1)
                    case 'T':
                        parameterTypes[i] = typeof(TParameter);
                        break;
                    //generic unserializable type (generic argument 2)
                    case 'U':
                        parameterTypes[i] = typeof(UParameter);
                        break;
                    //generic unserializable type (generic argument 3)
                    case 'V':
                        parameterTypes[i] = typeof(VParameter);
                        break;
                    //generic unserializable type (generic argument 4)
                    case 'W':
                        parameterTypes[i] = typeof(WParameter);
                        break;
                    //unserializable generic or non-generic open constructed type
                    case 't':
                        if (parameterType == null) parameterTypes[i] = typeof(OpenNonGenericType);
                        else if (parameterType.IsGenericType) parameterTypes[i] = parameterType.GetGenericTypeDefinition();
                        else parameterTypes[i] = typeof(OpenNonGenericType);
                        break;
                }
            }
        }

        mi = GetMethod(Type.GetType(data[0]), data[1], parameterTypes, methodIsGeneric);

        //if method is generic then we need to make a specific method from it's generic definition
        if (methodIsGeneric) {

            Type[] genericArguments = new Type[genericCount];

            for (int i = 0; i < genericCount; i++) {
                Type genericArgument = Type.GetType(data[i + 2 + parameterCount + 1]);
                genericArguments[i] = genericArgument;
            }

            try {
                mi = mi.MakeGenericMethod(genericArguments);
            }
            catch (Exception e) {
#if UNITY_EDITOR
                if (Application.isPlaying) Debug.LogError(e);
#endif
                return null;
            }

        }

        return mi;
    }

    private static readonly string[] TypeNameReplaced = new string[] { ", Version=", ", Culture=", ", PublicKeyToken=", ", Assembly-CSharp", ", mscorlib", ", UnityEngine." };

    private static readonly string[] TypeNameReplacing = new string[] { "", "", "", "", "", ", UnityEngine" };

    private static MethodInfo GetMethod(Type type, string name, Type[] types, bool methodIsGeneric) {

        MethodInfo method = null;
        Type baseType = type;
        do {
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try {
                if (!methodIsGeneric) {
                    method = baseType.GetMethod(name, bindingFlags, null, types, null);
                }
                else {
                    MethodInfo[] methods = baseType.GetMethods(bindingFlags);
                    for (int i = 0; i < methods.Length; i++) {
                        var m = methods[i];
                        if (m.Name != name) continue;
                        if (!m.IsGenericMethod) continue;
                        ParameterInfo[] parameters = m.GetParameters();
                        Type[] genericArguments = m.GetGenericArguments();
                        if (parameters.Length != types.Length) continue;
                        bool parametersMatch = true;
                        for (int j = 0; j < parameters.Length; j++) {
                            Type expectedParameter = types[j];
                            Type actualParameter = parameters[j].ParameterType;
                            //if parameter is generic parameter or open constructed type we do special check
                            if (actualParameter.ContainsGenericParameters) {
                                //if it is generic parameter
                                if (actualParameter.IsGenericParameter) {
                                    if (expectedParameter == typeof(TParameter) && actualParameter == genericArguments[0]) continue;
                                    else if (expectedParameter == typeof(UParameter) && actualParameter == genericArguments[1]) continue;
                                    else if (expectedParameter == typeof(VParameter) && actualParameter == genericArguments[2]) continue;
                                    else if (expectedParameter == typeof(WParameter) && actualParameter == genericArguments[3]) continue;
                                }
                                //if it's a generic unconstructed type
                                else {
                                    //if both are generic or nongeneric
                                    if (actualParameter.IsGenericType == expectedParameter.IsGenericType) {
                                        //if it's a typical generic type then we compare generic definitions
                                        if (actualParameter.IsGenericType) {
                                            if (actualParameter.GetGenericTypeDefinition() == expectedParameter) continue;
                                        }
                                        //if it's an open constructed non-generic type (usually array) we do special check
                                        else {
                                            if (expectedParameter == typeof(OpenNonGenericType)) continue;
                                        }
                                    }
                                }
                            }
                            //if paramter is not generic we only check if types match
                            else if (actualParameter == expectedParameter) continue;
                            parametersMatch = false;
                            break;
                        }
                        if (parametersMatch) {
                            method = m;
                            break;
                        }
                    }
                }
            }
            catch {
                return null;
            }
            if (method != null) break;
            baseType = baseType.BaseType;
        } while (baseType != typeof(object) && baseType != null);

        return method;
    }

    /// <summary>Represents non-generic open constructed type (such as generic array)</summary>
    private sealed class OpenNonGenericType {
    }

    /// <summary>Represents unserializable T parameter (first generic argument of a generic method)</summary>
    private sealed class TParameter {
    }

    /// <summary>Represents unserializable U parameter (second generic argument of a generic method)</summary>
    private sealed class UParameter {
    }

    /// <summary>Represents unserializable U parameter (third generic argument of a generic method)</summary>
    private sealed class VParameter {
    }

    /// <summary>Represents unserializable U parameter (fourth generic argument of a generic method)</summary>
    private sealed class WParameter {
    }
}