namespace SWDataExtractor.Core.Contratos;

[Flags]
public enum AlcanceExtraccion
{
    Ninguno     = 0,
    Propiedades = 1,
    Estructura  = 2,
    Fisicas     = 4,
    Preview     = 8,
    Features    = 16,
    Roscas      = 32,
    Rapida      = Propiedades | Estructura | Fisicas | Preview,
    Profunda    = Rapida | Features | Roscas
}

public enum EstadoExtraccion { Ok, Error, Timeout, VersionNoSoportada, Bloqueado, Omitido }

public enum TipoArchivoCad { Pieza, Ensamble, Plano, Otro }
