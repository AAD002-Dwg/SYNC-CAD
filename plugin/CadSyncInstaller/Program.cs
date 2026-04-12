using System;
using System.IO;
using System.Diagnostics;

namespace CadSyncInstaller
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Instalador CadSync";
            Console.WriteLine("====================================================");
            Console.WriteLine("       INSTALADOR DE COMPLEMENTO CADSYNC            ");
            Console.WriteLine("====================================================");
            Console.WriteLine();

            try
            {
                // 1. Determinar rutas
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string pluginsFolder = Path.Combine(appData, "Autodesk", "ApplicationPlugins");
                string destBundle = Path.Combine(pluginsFolder, "CadSync.bundle");

                // El origen es una carpeta llamada 'CadSync.bundle' que debe estar junto al EXE
                string sourceBundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CadSync.bundle");

                // Si no está junto al EXE (caso debug), probamos en el nivel superior
                if (!Directory.Exists(sourceBundle))
                {
                    sourceBundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "CadSync.bundle");
                }

                if (!Directory.Exists(sourceBundle))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR: No se encontró la carpeta 'CadSync.bundle'.");
                    Console.ResetColor();
                    Console.WriteLine("Asegúrate de que la carpeta del complemento esté junto a este instalador.");
                    Console.WriteLine("\nPresiona cualquier tecla para cerrar...");
                    Console.ReadKey();
                    return;
                }

                // 2. Preparar destino
                if (!Directory.Exists(pluginsFolder))
                {
                    Console.WriteLine("Creando carpeta de plugins de Autodesk...");
                    Directory.CreateDirectory(pluginsFolder);
                }

                // 3. Copiar archivos
                Console.WriteLine("Instalando archivos del complemento...");
                
                if (Directory.Exists(destBundle))
                {
                    Console.WriteLine("Actualizando versión existente...");
                    try { Directory.Delete(destBundle, true); } 
                    catch { Console.WriteLine("Aviso: No se pudo limpiar la carpeta anterior, intentando sobreescribir..."); }
                }

                Directory.CreateDirectory(destBundle);
                CopyFolder(sourceBundle, destBundle);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("====================================================");
                Console.WriteLine("  ¡INSTALACIÓN COMPLETADA CON ÉXITO!                ");
                Console.WriteLine("====================================================");
                Console.ResetColor();
                Console.WriteLine("\nEl complemento se cargará automáticamente la próxima vez que inicies AutoCAD.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nERROR DURANTE LA INSTALACIÓN: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\nPresiona cualquier tecla para salir...");
            Console.ReadKey();
        }

        static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }
            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyFolder(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }
    }
}
