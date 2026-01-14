import { defineConfig } from 'vitepress'

// https://vitepress.dev/reference/site-config
export default defineConfig({
  title: "EntglDb",
  description: "A lightweight, embeddable P2P database for .NET",
  base: "/EntglDb/", // Github Pages repository name usually
  themeConfig: {
    // https://vitepress.dev/reference/default-theme-config
    nav: [
      { text: 'Home', link: '/' },
      { text: 'Guide', link: '/getting-started' },
      { text: 'API Reference', link: '/api-reference' }
    ],

    sidebar: [
      {
        text: 'Introduction',
        items: [
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Architecture & Concepts', link: '/architecture' },
          { text: 'Security Disclaimer', link: '/architecture#security-disclaimer' }
        ]
      },
      {
        text: 'Reference',
        items: [
          { text: 'API Reference', link: '/api-reference' }
        ]
      }
    ],

    socialLinks: [
      { icon: 'github', link: 'https://github.com/lucafabbri/EntglDb' }
    ]
  }
})
