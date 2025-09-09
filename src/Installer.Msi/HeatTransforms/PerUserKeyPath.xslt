<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:wix="http://wixtoolset.org/schemas/v4/wxs">
  <xsl:output method="xml" indent="yes"/>

  <!-- Identity transform: copy everything by default -->
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()"/>
    </xsl:copy>
  </xsl:template>

  <!-- For each harvested Component, add an HKCU registry value as the KeyPath and neutralize File KeyPath -->
  <xsl:template match="wix:Component">
    <xsl:copy>
      <xsl:apply-templates select="@*"/>

      <!-- Insert RegistryValue KeyPath under HKCU so ICE38 is satisfied for per-user installs -->
      <wix:RegistryValue Root="HKCU" Name="Installed" Type="integer" Value="1" KeyPath="yes">
        <xsl:attribute name="Key">
          <xsl:text>Software\[Manufacturer]\[ProductName]\Components\</xsl:text>
          <xsl:value-of select="@Id"/>
        </xsl:attribute>
      </wix:RegistryValue>

      <!-- Process children next; a separate template will switch File@KeyPath to 'no' if present -->
      <xsl:apply-templates select="node()"/>
    </xsl:copy>
  </xsl:template>

  <!-- If Heat marked a File as KeyPath, switch it to 'no' since the registry value will be the KeyPath -->
  <xsl:template match="wix:File[@KeyPath='yes']">
    <xsl:copy>
      <!-- Copy all existing attributes except KeyPath -->
      <xsl:apply-templates select="@*[name()!='KeyPath']"/>
      <xsl:attribute name="KeyPath">no</xsl:attribute>
      <xsl:apply-templates select="node()"/>
    </xsl:copy>
  </xsl:template>

</xsl:stylesheet>