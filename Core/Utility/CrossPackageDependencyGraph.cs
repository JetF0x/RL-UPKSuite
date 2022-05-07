﻿using System.Text;
using Core.Classes.Core;
using Core.Types;
using Core.Types.PackageTables;

namespace Core.Utility;

public class PackageObjectReference : IEquatable<PackageObjectReference>
{
    /// <summary>
    ///     This references a native only class in a specific package
    /// </summary>
    public readonly UClass? NativeClass;

    /// <summary>
    ///     A index representing either a import or a export object from a package
    /// </summary>
    public readonly ObjectIndex ObjectIndex;

    /// <summary>
    ///     The name of the package that the ObjectIndex originates from
    /// </summary>
    public readonly string PackageName;


    /// <summary>
    ///     References a unique object in a package
    /// </summary>
    /// <param name="packageName"></param>
    /// <param name="objectIndex"></param>
    public PackageObjectReference(string packageName, ObjectIndex objectIndex)
    {
        ObjectIndex = objectIndex;
        PackageName = packageName;
        NativeClass = null;
    }

    /// <summary>
    ///     A package reference for a native only class object with no export\import table entry
    /// </summary>
    /// <param name="packageName"></param>
    /// <param name="nativeClass"></param>
    public PackageObjectReference(string packageName, UClass nativeClass)
    {
        ObjectIndex = new ObjectIndex();
        PackageName = packageName;
        NativeClass = nativeClass;
    }

    /// <inheritdoc />
    public bool Equals(PackageObjectReference? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Equals(NativeClass, other.NativeClass) && ObjectIndex.Equals(other.ObjectIndex) && PackageName == other.PackageName;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((PackageObjectReference) obj);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(NativeClass, ObjectIndex, PackageName);
    }
}

/// <summary>
///     Helper class to construct a complete dependency graph, including all required objects from imported packages
/// </summary>
public class CrossPackageDependencyGraph
{
    private readonly Dictionary<PackageObjectReference, HashSet<Edge>> _adj = new();

    private readonly IImportResolver _packageImportResolver;

    /// <summary>
    ///     Create a package dependency graph. A import resolver is required to resolve import object dependencies correctly.
    /// </summary>
    /// <param name="packageImportResolver"></param>
    public CrossPackageDependencyGraph(IImportResolver packageImportResolver)
    {
        _packageImportResolver = packageImportResolver;
    }

    /// <summary>
    ///     How many objects in the graph
    /// </summary>
    public int NodeCount => _adj.Count;


    /// <summary>
    ///     Add a object to the graph.
    /// </summary>
    /// <param name="node">The new node</param>
    public void AddNode(PackageObjectReference node)
    {
        if (_adj.ContainsKey(node))
        {
            // Fail silently.
            return;
        }

        _adj[node] = new HashSet<Edge>();
    }

    private bool ImportIsNative(ImportTableItem importItem, UnrealPackage package)
    {
        return package.GetName(package.GetImportPackage(importItem).ObjectName) == package.PackageName;
    }


    private PackageObjectReference ResolveImportObjectReference(ImportTableItem import, UnrealPackage objPackage)
    {
        var importPackageReference = objPackage.GetImportPackage(import);
        var packageName = objPackage.GetName(importPackageReference.ObjectName);
        var importPackage = _packageImportResolver.ResolveExportPackage(packageName);
        var fullName = objPackage.GetFullName(import);
        if (importPackage is null)
        {
            throw new InvalidOperationException($"Can't find the package to resolve dependencies for {fullName}");
        }

        var nameParts = fullName.Split('.');
        var exportFullNameMatch =
            importPackage.ExportTable.FirstOrDefault(x => importPackage.GetName(x.ObjectName) == nameParts[^1] && importPackage.GetFullName(x) == fullName);
        if (exportFullNameMatch is not null)
        {
            var exportIndex = new ObjectIndex(ObjectIndex.FromExportIndex(importPackage.ExportTable.IndexOf(exportFullNameMatch)));
            return new PackageObjectReference(packageName, exportIndex);
        }

        var importFullNameMatch =
            importPackage.ImportTable.FirstOrDefault(x => importPackage.GetName(x.ObjectName) == nameParts[^1] && importPackage.GetFullName(x) == fullName);
        if (importFullNameMatch is not null)
        {
            var importIndex = new ObjectIndex(ObjectIndex.FromImportIndex(importPackage.ImportTable.IndexOf(importFullNameMatch)));
            return new PackageObjectReference(packageName, importIndex);
        }

        var nativeClass = importPackage.FindClass(objPackage.GetName(import.ObjectName));
        if (nativeClass is not null)
        {
            return new PackageObjectReference(packageName, nativeClass);
        }

        throw new InvalidOperationException($"Failed to find the import object: {objPackage.GetFullName(import)}");
    }

    public void AddObjectDependencies(PackageObjectReference objReference)
    {
        var objQueue = new Queue<PackageObjectReference>();
        objQueue.Enqueue(objReference);
        AddNode(objReference);

        while (objQueue.Count != 0)
        {
            var currentObj = objQueue.Dequeue();
            var objPackage = _packageImportResolver.ResolveExportPackage(currentObj.PackageName);
            if (objPackage is null)
            {
                throw new InvalidOperationException($"Can't find the package to resolve dependencies for {currentObj.PackageName}");
            }

            var obj = objPackage.GetObjectReference(currentObj.ObjectIndex);
            var fullName = objPackage.GetFullName(obj);
            if (obj is ImportTableItem import)
            {
                if (import.OuterIndex.Index != 0)
                {
                    // Native imports has no export reference to resolve
                    var outerReference = new PackageObjectReference(currentObj.PackageName, import.OuterIndex);
                    AddEdge(outerReference, currentObj);
                    objQueue.Enqueue(outerReference);
                    if (!ImportIsNative(import, objPackage))
                    {
                        var exportReference = ResolveImportObjectReference(import, objPackage);
                        AddEdge(exportReference, currentObj);
                        if (exportReference.NativeClass is null)
                        {
                            objQueue.Enqueue(exportReference);
                        }
                    }
                }
            }
            else if (obj is ExportTableItem export)
            {
                if (export.OuterIndex.Index != 0)
                {
                    var outerReference = new PackageObjectReference(currentObj.PackageName, export.OuterIndex);
                    AddEdge(outerReference, currentObj);
                    objQueue.Enqueue(outerReference);
                }

                if (export.ClassIndex.Index != 0)
                {
                    var classReference = new PackageObjectReference(currentObj.PackageName, export.ClassIndex);
                    AddEdge(classReference, currentObj);
                    objQueue.Enqueue(classReference);
                }

                if (export.SuperIndex.Index != 0)
                {
                    var supereReference = new PackageObjectReference(currentObj.PackageName, export.SuperIndex);
                    AddEdge(supereReference, currentObj);
                    objQueue.Enqueue(supereReference);
                }

                if (export.ArchetypeIndex.Index != 0)
                {
                    var archetypeReference = new PackageObjectReference(currentObj.PackageName, export.ArchetypeIndex);
                    AddEdge(archetypeReference, currentObj);
                    objQueue.Enqueue(archetypeReference);
                }
            }
        }
    }

    // A recursive function used by topologicalSort
    private void TopologicalSortUtil(PackageObjectReference v, ISet<PackageObjectReference> visited,
        Stack<PackageObjectReference> stack)
    {
        // Mark the current node as visited.
        visited.Add(v);

        // Recur for all the vertices
        // adjacent to this vertex
        foreach (var vertex in _adj[v])
        {
            if (!visited.Contains(vertex.Dest))
            {
                TopologicalSortUtil(vertex.Dest, visited, stack);
            }
        }

        // Push current vertex to
        // stack which stores result
        stack.Push(v);
    }

    /// <summary>
    ///     https://www.geeksforgeeks.org/topological-sorting/
    ///     The function to do Topological Sort.
    ///     It uses recursive topologicalSortUtil()
    ///     MVN: I slightly modified the implementation to be compatible with my use case
    /// </summary>
    /// <returns></returns>
    public List<PackageObjectReference> TopologicalSort()
    {
        Stack<PackageObjectReference> stack = new();

        // Mark all the vertices as not visited
        var visited = new HashSet<PackageObjectReference>();

        // Call the recursive helper function
        // to store Topological Sort starting
        // from all vertices one by one
        foreach (var (key, _) in _adj)
        {
            if (!visited.Contains(key))
            {
                TopologicalSortUtil(key, visited, stack);
            }
        }

        return stack.ToList();
    }

    public string GetReferenceFullName(PackageObjectReference objectReference)
    {
        if (objectReference.NativeClass is not null)
        {
            return
                $"{objectReference.PackageName} : ({objectReference.NativeClass.Class.Name}) {objectReference.PackageName}.{objectReference.NativeClass.Name}";
        }

        var importPackage = _packageImportResolver.ResolveExportPackage(objectReference.PackageName);
        ArgumentNullException.ThrowIfNull(importPackage);
        var obj = importPackage.GetObjectReference(objectReference.ObjectIndex);
        string? objTypeName;
        switch (obj)
        {
            case ExportTableItem exportTableItem:
                if (exportTableItem.ClassIndex.Index == 0)
                {
                    objTypeName = "Class";
                }
                else
                {
                    var objectResource = importPackage.GetObjectReference(exportTableItem.ClassIndex);
                    objTypeName = importPackage.GetName(objectResource.ObjectName);
                }

                break;
            case ImportTableItem importTableItem:
                objTypeName = importPackage.GetName(importTableItem.ClassName);
                break;
            default:
                objTypeName = "unknown";
                break;
        }

        return obj == null ? "null" : $"{objectReference.PackageName}: ({objTypeName}) {importPackage?.GetFullName(obj) ?? "null"}";
    }

    public string GetGraphDebugLines()
    {
        var sb = new StringBuilder();
        foreach (var (node, edges) in _adj)
        {
            sb.AppendLine($"{GetReferenceFullName(node)}");
            foreach (var edge in edges)
            {
                sb.AppendLine($"\t{GetReferenceFullName(edge.Dest)}");
            }
        }

        return sb.ToString();
    }


    /// <summary>
    ///     Add a Edge representing a dependency between from and to. If any of the nodes are new. Add them
    /// </summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    public void AddEdge(PackageObjectReference from, PackageObjectReference to)
    {
        if (from.Equals(to))
        {
            throw new ArgumentException("Can't create a edge between the same two nodes");
        }

        if (!_adj.ContainsKey(from))
        {
            AddNode(from);
        }

        if (!_adj.ContainsKey(to))
        {
            AddNode(to);
        }

        _adj[from].Add(new Edge(to));
    }

    /// <summary>
    ///     Get the edges from a node. This represents the objects that depends on this node. Throws if the node is not
    ///     registered to the graph
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    public List<Edge> GetEdges(PackageObjectReference node)
    {
        return _adj[node].ToList();
    }

    public class Edge : IEquatable<Edge>
    {
        public Edge(PackageObjectReference dest)
        {
            Dest = dest;
        }

        public PackageObjectReference Dest { get; set; }

        /// <inheritdoc />
        public bool Equals(Edge? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Dest.Equals(other.Dest);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((Edge) obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Dest.GetHashCode();
        }
    }
}