#ifdef GL_ES
precision mediump float;
#endif

uniform sampler2D Texture0;
uniform sampler2D Texture1;
uniform sampler2D Texture2;
uniform sampler2D Texture3;
uniform sampler2D Texture4;
uniform sampler2D Texture5;
uniform sampler2D Texture6;
uniform sampler2D Texture7;
uniform sampler2D Palette;

uniform bool EnableDepthPreview;
uniform float DepthTextureScale;

varying vec4 vTexCoord;
varying vec2 vTexMetadata;
varying vec4 vChannelMask;
varying vec4 vDepthMask;
varying vec2 vTexSampler;

varying vec4 vColorFraction;
varying vec4 vRGBAFraction;
varying vec4 vPalettedFraction;

float jet_r(float x)
{
	return x < 0.7 ? 4.0 * x - 1.5 : -4.0 * x + 4.5;
}

float jet_g(float x)
{
	return x < 0.5 ? 4.0 * x - 0.5 : -4.0 * x + 3.5;
}

float jet_b(float x)
{
	return x < 0.3 ? 4.0 * x + 0.5 : -4.0 * x + 2.5;
}

vec4 Sample(float samplerIndex, vec2 pos)
{
	if (samplerIndex < 1.0)
		return texture2D(Texture0, pos);
	else if (samplerIndex < 2.0)
		return texture2D(Texture1, pos);
	else if (samplerIndex < 3.0)
		return texture2D(Texture2, pos);
	else if (samplerIndex < 4.0)
		return texture2D(Texture3, pos);
	else if (samplerIndex < 5.0)
		return texture2D(Texture4, pos);
	else if (samplerIndex < 6.0)
		return texture2D(Texture5, pos);
	else if (samplerIndex < 7.0)
		return texture2D(Texture6, pos);

	return texture2D(Texture7, pos);
}

void main()
{
	vec4 x = Sample(vTexSampler.s, vTexCoord.st);
	vec2 p = vec2(dot(x, vChannelMask), vTexMetadata.s);
	vec4 c = vPalettedFraction * texture2D(Palette, p) + vRGBAFraction * x + vColorFraction * vTexCoord;
	gl_FragColor = c;
}
