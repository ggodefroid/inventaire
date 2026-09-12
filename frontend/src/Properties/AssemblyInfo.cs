using System.Reflection;

// AssemblyFileVersion n'existe pas dans le mscorlib du Compact Framework :
// c'est un attribut du .NET de bureau. La passe de controle de surface d'API
// l'attrape avant le terminal.
[assembly: AssemblyTitle("Inventaire")]
[assembly: AssemblyDescription("Inventaire du frigo - client terminal code-barres")]
[assembly: AssemblyProduct("Inventaire")]
[assembly: AssemblyVersion("1.0.0.0")]
